import { deps } from "@/lib/bff/deps"
import { handleLogout } from "@/lib/bff/handlers"

export const dynamic = "force-dynamic"

export function POST(request: Request) {
  return handleLogout(request, deps())
}
