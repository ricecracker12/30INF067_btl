"use client"

import { useCallback, useRef, useState } from "react"

import { contentApi } from "@/lib/api/content-api"
import { fieldMessage, R2_PUT_FAILED } from "@/lib/api/messages"
import type {
  ImageContentType,
  MediaKeyDeclaration,
  UploadTicket,
} from "@/lib/api/types"
import { putToR2 } from "@/lib/upload/r2"
import { IMAGE_NOT_ALLOWED, imageFileError } from "@/lib/validation/media"
import { MAX_MEDIA_COUNT, TOO_MANY_MEDIA } from "@/lib/validation/post"

// Hàng đợi tải ảnh của composer (E4 bước 3). Luồng ba bước của E3 chạy với MỘT ảnh; ở đây nó chạy với
// tối đa mười, và ba thứ mới xuất hiện cùng lúc: một lời gọi presign cho CẢ LÔ, song song có trần, và
// MỘT ảnh hỏng không được kéo chín ảnh kia theo.
//
// Vì sao một hook chứ không một hàm thuần trong `lib/`: hàng đợi biết nghiệp vụ (`purpose: "post"`,
// `mediaKeys` của `POST /posts`) nên nó thuộc `features/post/` (Đ-E13); và mỗi bước tiến trình là một
// lần render, nên trạng thái phải là state của React chứ không phải biến ngoài.

/** Bốn trạng thái của một dòng ảnh, đúng bằng bốn thứ người dùng thấy (E4 bước 3). */
export type UploadStatus = "cho" | "dang-gui" | "xong" | "loi"

export type UploadItem = {
  /** Định danh CỤC BỘ, sống qua cả lượt thử lại — `mediaKey` thì không: presign lại sinh key mới. */
  id: string
  file: File
  /** Đã qua `imageFileError` nên chắc chắn thuộc allowlist; khai lúc presign và gửi lại ở `POST /posts`. */
  contentType: ImageContentType
  status: UploadStatus
  /** 0–100. Chỉ có nghĩa khi `dang-gui`; `xong` luôn là 100. */
  percent: number
  /** Câu cho người dùng khi `loi` — mỗi bước một câu, xem `presignBatch` và `sendOne`. */
  error?: string
  ticket?: UploadTicket
  /** `Date.now()` lúc NHẬN ticket — để lượt thử lại biết `uploadUrl` còn hạn hay phải presign lại. */
  ticketAt?: number
}

/** Trần song song (E4 bước 3). Mười lượt `PUT` cùng lúc thì thanh nào cũng bò, và mạng yếu thì timeout cả lô. */
const MAX_PARALLEL = 3

/**
 * Trừ hao trước khi coi `uploadUrl` là còn hạn. `expiresIn` là 600 giây kể từ lúc SERVER ký, còn
 * `ticketAt` đo lúc trình duyệt NHẬN — và một ảnh 10 MB trên mạng chậm mất vài chục giây nữa. Bắt đầu một
 * lượt `PUT` sát hạn thì chữ ký chết giữa chừng và người dùng nhận đúng câu lỗi của ca CORS/CSP, tức là
 * nghi phạm sai. Trừ hao chỉ làm ta presign lại (một request rẻ), không bao giờ chặn người dùng.
 */
const TICKET_SAFETY_MS = 60_000

/** Hợp đồng: một ticket cho mỗi file, cùng thứ tự. Thiếu là hợp đồng vỡ, không phải ca người dùng. */
const PRESIGN_MISSING_TICKET = "Không xin được lượt tải lên cho ảnh này."

export type UploadQueue = {
  items: UploadItem[]
  /** Lỗi của CHÍNH LƯỢT CHỌN (sai loại, quá nặng, quá 10 ảnh) — hiện dưới ô chọn ảnh, không phải dưới form. */
  fileError?: string
  /** Thêm ảnh: kiểm từng file, rồi MỘT lời gọi presign cho cả lô và bắt đầu tải. */
  addFiles: (files: readonly File[]) => Promise<void>
  /** Bỏ một ảnh khỏi bài. Đã tải lên rồi thì KHÔNG đụng gì tới R2 — worker dọn mồ côi sau (Đ-2.13). */
  removeItem: (id: string) => void
  /** Thử lại RIÊNG một ảnh lỗi; chín ảnh kia không bị đụng tới. */
  retryItem: (id: string) => Promise<void>
  /** Mọi ảnh đã `xong` (bài không ảnh cũng đúng) — điều kiện bật nút Đăng. */
  allDone: boolean
  /** Ba giá trị mỗi ảnh, ĐÚNG BẰNG thứ đã khai lúc presign; thứ tự mảng là `position` hiển thị (0..9). */
  mediaKeys: () => MediaKeyDeclaration[]
  /** Dọn sạch sau khi đăng xong. */
  reset: () => void
}

export function useUploadQueue(): UploadQueue {
  const [items, setItems] = useState<UploadItem[]>([])
  const [fileError, setFileError] = useState<string | undefined>(undefined)

  // Bản sao ĐỒNG BỘ của `items`. Vòng bơm và các closure `async` bên dưới đọc trạng thái mới nhất ngay
  // trong cùng một lượt: `setItems` không cập nhật kịp, nên chỉ đọc `items` là hai lượt `PUT` cùng nhận
  // một ảnh. MỌI thay đổi đi qua `commit`, không nơi nào gọi thẳng `setItems`.
  const itemsRef = useRef<UploadItem[]>([])
  const runningRef = useRef(0)

  const commit = useCallback((next: UploadItem[]) => {
    itemsRef.current = next
    setItems(next)
  }, [])

  const patch = useCallback(
    (id: string, changes: Partial<UploadItem>) => {
      commit(
        itemsRef.current.map((item) =>
          item.id === id ? { ...item, ...changes } : item
        )
      )
    },
    [commit]
  )

  /**
   * Bước 2 cho MỘT ảnh. Cả hai kết cục hỏng của `putToR2` (R2 từ chối, và mạng/CORS/CSP) ra CÙNG một câu:
   * người dùng làm cùng một việc — bấm "Thử lại" — và trình duyệt cố ý không cho biết là cái nào.
   */
  const sendOne = useCallback(
    async (id: string) => {
      const item = itemsRef.current.find((i) => i.id === id)
      if (!item?.ticket) return

      try {
        await putToR2({
          url: item.ticket.uploadUrl,
          file: item.file,
          // Giá trị ĐÃ NẰM TRONG chữ ký — dùng lại cái server ký, không dựng lại từ `file.type`.
          contentType: item.ticket.requiredHeaders["Content-Type"],
          onProgress: (loaded, total) =>
            patch(id, {
              percent: total > 0 ? Math.round((loaded / total) * 100) : 0,
            }),
        })
        patch(id, { status: "xong", percent: 100, error: undefined })
      } catch {
        patch(id, { status: "loi", error: R2_PUT_FAILED })
      }
    },
    [patch]
  )

  /**
   * Nhận ảnh chờ kế tiếp và ĐÁNH DẤU NGAY là `dang-gui`. Đánh dấu đồng bộ (qua `patch`, tức là vào
   * `itemsRef`) mới là chỗ chặn lấy trùng: lượt `find` tiếp theo — của chính vòng bơm hay của một thợ
   * khác — không còn thấy ảnh vừa nhận nữa.
   */
  const claimNext = useCallback((): string | undefined => {
    const next = itemsRef.current.find(
      (item) => item.status === "cho" && item.ticket
    )
    if (!next) return undefined

    patch(next.id, { status: "dang-gui", percent: 0 })
    return next.id
  }, [patch])

  /** Một "thợ": làm xong một ảnh thì tự nhận ảnh kế tiếp, hết ảnh thì nghỉ và trả lại một suất song song. */
  const runWorker = useCallback(
    async (firstId: string) => {
      let id: string | undefined = firstId
      while (id !== undefined) {
        await sendOne(id)
        id = claimNext()
      }
      runningRef.current -= 1
    },
    [claimNext, sendOne]
  )

  /** Mở thêm thợ cho tới trần `MAX_PARALLEL`. Gọi được nhiều lần: hết ảnh chờ thì nó không mở gì. */
  const pump = useCallback(() => {
    while (runningRef.current < MAX_PARALLEL) {
      const id = claimNext()
      if (id === undefined) return

      runningRef.current += 1
      void runWorker(id)
    }
  }, [claimNext, runWorker])

  /**
   * Bước 1 cho cả lô: **MỘT** lời gọi `POST /media/uploads` cho tối đa 10 ảnh (Đ-2.15). Gọi mười lần là
   * mười phần trăm hạn mức 100 req/phút cho một bài.
   *
   * Hỏng cả lô thì mọi ảnh trong lô thành `loi` với CÙNG câu — chúng hỏng vì cùng một lý do (quyền, hạn
   * mức, mạng). Lượt thử lại sau đó presign lại từng ảnh một, vì lúc đó chỉ còn ảnh đó cần.
   */
  const presignBatch = useCallback(
    async (ids: readonly string[]) => {
      const batch = itemsRef.current.filter((item) => ids.includes(item.id))
      if (batch.length === 0) return

      let tickets: UploadTicket[]
      try {
        tickets = await contentApi.createUploads({
          purpose: "post",
          files: batch.map((item) => ({
            contentType: item.contentType,
            // Dung lượng THẬT của file. GĐ2 không cắt/nén/strip EXIF ở client (hoãn tới GĐ7), nên con số
            // này còn đúng lúc server `HEAD` lên R2 đối chiếu (Đ-2.8 lớp 2).
            sizeBytes: item.file.size,
          })),
        })
      } catch (error) {
        // 400 của presign hiện ĐÚNG CÂU SERVER dưới key `files` — nó nói được thứ bảng chung không nói
        // được; không có key thì lùi về bảng `upload`.
        const message = fieldMessage(error, "files", "upload")
        commit(
          itemsRef.current.map((item) =>
            ids.includes(item.id)
              ? { ...item, status: "loi", error: message }
              : item
          )
        )
        return
      }

      const at = Date.now()
      commit(
        itemsRef.current.map((item) => {
          // Ticket ghép với file theo INDEX TRONG LÔ, không theo index của `items`: lô này có thể là lượt
          // chọn thứ hai, hoặc một ảnh thử lại giữa chín ảnh cũ.
          const index = batch.findIndex((b) => b.id === item.id)
          if (index < 0) return item

          const ticket = tickets[index]
          if (!ticket)
            return { ...item, status: "loi", error: PRESIGN_MISSING_TICKET }

          return {
            ...item,
            ticket,
            ticketAt: at,
            status: "cho",
            error: undefined,
          }
        })
      )
      pump()
    },
    [commit, pump]
  )

  const addFiles = useCallback(
    async (files: readonly File[]) => {
      setFileError(undefined)
      if (files.length === 0) return

      // Lọc từng file TRƯỚC khi gọi API: một file sai loại làm hỏng cả lời gọi presign của chín file kia
      // (server từ chối cả request), mà nguyên nhân thì nằm ở một file người dùng chọn nhầm.
      const accepted = files.filter((file) => !imageFileError(file))
      // Thêm phần hợp lệ và báo phần bị bỏ, thay vì từ chối cả lượt chọn: chọn 10 ảnh mà một cái là GIF
      // thì bắt chọn lại cả 10 là phạt người dùng vì một lần bấm nhầm.
      if (accepted.length < files.length) setFileError(IMAGE_NOT_ALLOWED)
      if (accepted.length === 0) return

      // Quá 10 thì KHÔNG thêm gì từ lượt này — cắt bớt cho vừa là tự chọn hộ người dùng ảnh nào bị bỏ.
      if (itemsRef.current.length + accepted.length > MAX_MEDIA_COUNT) {
        setFileError(TOO_MANY_MEDIA)
        return
      }

      const added: UploadItem[] = accepted.map((file) => ({
        id: crypto.randomUUID(),
        file,
        // `imageFileError` đã thu hẹp `file.type` về allowlist ở trên.
        contentType: file.type as ImageContentType,
        status: "cho",
        percent: 0,
      }))
      commit([...itemsRef.current, ...added])
      await presignBatch(added.map((item) => item.id))
    },
    [commit, presignBatch]
  )

  const removeItem = useCallback(
    (id: string) => {
      setFileError(undefined)
      // Ảnh đã `xong` thì object đã nằm trên R2 và FE KHÔNG làm gì với nó (Đ-2.13): không có endpoint xóa
      // cho client, và bỏ key ra khỏi `mediaKeys` là đủ để nó thành mồ côi cho worker dọn sau 24 giờ.
      commit(itemsRef.current.filter((item) => item.id !== id))
    },
    [commit]
  )

  const retryItem = useCallback(
    async (id: string) => {
      const item = itemsRef.current.find((i) => i.id === id)
      if (!item || item.status !== "loi") return

      const fresh =
        item.ticket !== undefined &&
        item.ticketAt !== undefined &&
        Date.now() - item.ticketAt <
          item.ticket.expiresIn * 1000 - TICKET_SAFETY_MS

      if (fresh) {
        // Còn hạn → `PUT` lại CÙNG URL, giữ nguyên `mediaKey`. Không tốn một lượt presign, và quan trọng
        // hơn: key không đổi nên không sinh thêm một object mồ côi trên R2.
        patch(id, { status: "cho", percent: 0, error: undefined })
        pump()
        return
      }

      // Hết hạn → presign lại ĐÚNG MỘT file và nhận `mediaKey` MỚI. Xóa ticket cũ trước để không có cửa
      // nào gửi `mediaKey` cũ lên `POST /posts`: object của key đó chưa bao giờ tồn tại, server `HEAD`
      // không thấy và trả 400 "Ảnh chưa được tải lên xong…".
      patch(id, {
        status: "cho",
        percent: 0,
        error: undefined,
        ticket: undefined,
        ticketAt: undefined,
      })
      await presignBatch([id])
    },
    [patch, presignBatch, pump]
  )

  const mediaKeys = useCallback((): MediaKeyDeclaration[] => {
    const seen = new Set<string>()
    const result: MediaKeyDeclaration[] = []

    for (const item of itemsRef.current) {
      if (item.status !== "xong" || !item.ticket) continue
      // Khử trùng theo `mediaKey`: mỗi lượt presign sinh key mới nên hai key giống nhau chỉ xảy ra khi
      // thử lại sai đường — nhưng server trả 400 `errors.mediaKeys` cho ca đó, và câu đó nói về "đính kèm
      // hai lần" chứ không nói về thứ vừa xảy ra thật.
      if (seen.has(item.ticket.mediaKey)) continue
      seen.add(item.ticket.mediaKey)

      result.push({
        mediaKey: item.ticket.mediaKey,
        // ĐÚNG BẰNG thứ đã khai lúc presign và đúng bằng file thật — server `HEAD` lên R2 rồi đối chiếu
        // cả ba giá trị (Đ-2.8 lớp 2).
        contentType: item.contentType,
        sizeBytes: item.file.size,
      })
    }

    return result
  }, [])

  const reset = useCallback(() => {
    setFileError(undefined)
    commit([])
  }, [commit])

  return {
    items,
    fileError,
    addFiles,
    removeItem,
    retryItem,
    allDone: items.every((item) => item.status === "xong"),
    mediaKeys,
    reset,
  }
}
