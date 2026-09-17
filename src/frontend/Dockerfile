# syntax=docker/dockerfile:1
# E8 — image frontend (Next.js standalone + BFF, Đ-E14). Context = src/frontend/.
# Staging là OCI Ampere: build bằng --platform linux/arm64 (AGENTS.md luật 8).
#
# KHÔNG có biến nào lúc build (Đ-E14): trình duyệt chỉ gọi /bff cùng origin; cấu hình BFF là biến LÚC CHẠY —
# API_INTERNAL_URL, REDIS_URL, APP_ORIGIN, SESSION_ENCRYPTION_KEY, TRUSTED_PROXY_HOPS (src/frontend/.env.example).
# Một image dùng cho mọi môi trường; không bí mật nào nằm trong lớp image.

# ---------- Cài dependency (tách lớp để đổi code không phải cài lại) ----------
FROM node:24-alpine AS deps
WORKDIR /app
# pnpm đúng bản ghim ở trường "packageManager" (Đ-E9).
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
RUN pnpm install --frozen-lockfile

# ---------- Build ----------
FROM node:24-alpine AS build
WORKDIR /app
RUN corepack enable
COPY --from=deps /app/node_modules ./node_modules
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
RUN pnpm build

# ---------- Runtime ----------
FROM node:24-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    # Nghe mọi giao diện: thiếu dòng này server.js chỉ nghe trong container, apache trên host không vào được.
    HOSTNAME=0.0.0.0

# Chạy non-root.
RUN addgroup -S app && adduser -S app -G app

# standalone KHÔNG tự mang public/ và .next/static/ — thiếu là trang lên mà không có CSS/JS.
COPY --from=build --chown=app:app /app/public ./public
COPY --from=build --chown=app:app /app/.next/standalone ./
COPY --from=build --chown=app:app /app/.next/static ./.next/static

USER app
EXPOSE 3000

# Gọi /login của CHÍNH Next (không /health — apache chuyển /health về API, Đ-E11). Kiểm tiến trình sống và render được;
# không kiểm Redis/API: chúng chết thì /bff trả 503/502 rõ ràng, còn restart container không chữa được chúng.
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:3000/login > /dev/null || exit 1

CMD ["node", "server.js"]
