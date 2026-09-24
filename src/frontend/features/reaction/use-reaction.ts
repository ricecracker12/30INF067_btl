"use client"

import { useCallback, useRef, useState } from "react"

import { contentApi, type ReactionTarget } from "@/lib/api/content-api"
import { errorMessage } from "@/lib/api/messages"
import type { ReactionSummary, ReactionType } from "@/lib/api/types"

import {
  failed,
  initialReaction,
  press,
  settled,
  view,
  type ReactionState,
  type Send,
} from "./reaction-reducer"

/**
 * Nối reducer thuần (Đ-3.13) với `contentApi.react`. Trạng thái THẬT nằm trong `stateRef` — cập nhật đồng bộ ngay trong handler
 * và trong callback của request, không đợi render — để hai lần bấm sát nhau không đọc cùng một bản cũ. `useState` chỉ để vẽ lại.
 *
 * Khởi tạo MỘT lần từ `initial` (bài/bình luận vừa nạp): từ đó `confirmed` của reducer là nguồn đúng hơn số trong một trang nạp
 * lại sau (rủi ro GĐ4-02 — feed nạp lại không làm số "nhảy lùi"). Đổi đối tượng thì màn đổi `key`.
 */
export function useReaction(target: ReactionTarget, initial: ReactionSummary) {
  const [state, setState] = useState<ReactionState>(() =>
    initialReaction(initial)
  )
  const [error, setError] = useState<string | null>(null)
  const stateRef = useRef(state)
  const targetRef = useRef(target)

  const commit = useCallback((next: ReactionState) => {
    stateRef.current = next
    setState(next)
  }, [])

  const dispatch = useCallback(
    (first: Send) => {
      // Chuỗi request TUẦN TỰ: request sau chỉ đi khi request trước đã về (bước 4 của Đ-3.13).
      const go = (send: Send) => {
        if (send === null) return
        contentApi.react(targetRef.current, send.type).then(
          (summary) => {
            const step = settled(stateRef.current, summary)
            commit(step.state)
            go(step.send)
          },
          (e: unknown) => {
            commit(failed(stateRef.current))
            setError(errorMessage("reaction", e))
          }
        )
      }
      go(first)
    },
    [commit]
  )

  const choose = useCallback(
    (next: ReactionType | null) => {
      setError(null)
      const step = press(stateRef.current, next)
      commit(step.state)
      dispatch(step.send)
    },
    [commit, dispatch]
  )

  return { summary: view(state), pending: state.inFlight, error, choose }
}
