import { PostDetail } from "@/features/post/post-detail"

// Chi tiết một bài (Q-E6). Chỉ ráp (Đ-E13); mọi phân nhánh 404 / lỗi / skeleton nằm trong `PostDetail`.
export default async function PostPage({
  params,
}: {
  params: Promise<{ postId: string }>
}) {
  const { postId } = await params
  return <PostDetail postId={postId} />
}
