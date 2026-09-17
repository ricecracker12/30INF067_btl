import "server-only"

import {
  createCipheriv,
  createDecipheriv,
  createHash,
  randomBytes,
} from "node:crypto"

// Đ-E14 — token trong Redis không nằm dạng rõ: bản dump Redis, lệnh MONITOR hay một container khác trong mạng đọc được
// Redis đều không lấy được token nếu không có SESSION_ENCRYPTION_KEY (chỉ nằm trong môi trường tiến trình Next).

const IV_BYTES = 12
const TAG_BYTES = 16
const SESSION_ID = /^[A-Za-z0-9_-]{43}$/

/** 32 byte ngẫu nhiên, base64url (43 ký tự) — thứ duy nhất trình duyệt giữ. */
export function newSessionId(): string {
  return randomBytes(32).toString("base64url")
}

/** Cookie là dữ liệu người dùng gửi: sai dạng thì không bao giờ chạm tới Redis. */
export function isSessionId(value: string | null | undefined): value is string {
  return typeof value === "string" && SESSION_ID.test(value)
}

/** Key Redis là BĂM của session ID — dump Redis không ra được cookie để dùng lại. */
export function hashSessionId(sid: string): string {
  return createHash("sha256").update(sid).digest("hex")
}

/**
 * AES-256-GCM. `aad` gắn bản mã với đúng key Redis của nó: chép giá trị của phiên này sang key của phiên khác thì giải
 * mã thất bại, không "mượn" được phiên người khác.
 */
export function seal(plaintext: string, key: Buffer, aad: string): string {
  const iv = randomBytes(IV_BYTES)
  const cipher = createCipheriv("aes-256-gcm", key, iv)
  cipher.setAAD(Buffer.from(aad, "utf8"))
  const body = Buffer.concat([cipher.update(plaintext, "utf8"), cipher.final()])
  return Buffer.concat([iv, cipher.getAuthTag(), body]).toString("base64url")
}

/** `null` khi sai khóa, sai aad, hoặc bị sửa — không ném, người gọi coi như không có phiên. */
export function open(sealed: string, key: Buffer, aad: string): string | null {
  try {
    const raw = Buffer.from(sealed, "base64url")
    if (raw.length < IV_BYTES + TAG_BYTES + 1) return null
    const decipher = createDecipheriv(
      "aes-256-gcm",
      key,
      raw.subarray(0, IV_BYTES)
    )
    decipher.setAAD(Buffer.from(aad, "utf8"))
    decipher.setAuthTag(raw.subarray(IV_BYTES, IV_BYTES + TAG_BYTES))
    return Buffer.concat([
      decipher.update(raw.subarray(IV_BYTES + TAG_BYTES)),
      decipher.final(),
    ]).toString("utf8")
  } catch {
    return null
  }
}
