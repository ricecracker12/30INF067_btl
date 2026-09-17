import { deps } from "@/lib/bff/deps"
import { handlePublicAuth } from "@/lib/bff/handlers"

export const dynamic = "force-dynamic"

export function POST(request: Request) {
  return handlePublicAuth(request, deps(), "/auth/verify-email")
}
