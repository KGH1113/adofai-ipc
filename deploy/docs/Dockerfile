# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /app
RUN npm install --global pnpm@11.7.0
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY packages/adofai-ipc/package.json packages/adofai-ipc/package.json
COPY packages/typespec/package.json packages/typespec/package.json
COPY docs/package.json docs/package.json
RUN pnpm install --frozen-lockfile --filter @adofai-ipc/docs
COPY docs ./docs
ARG BUILD_SHA
RUN test -n "$BUILD_SHA" && echo "$BUILD_SHA" | grep -Eq '^[0-9a-f]{40}$' \
    && pnpm docs:build \
    && printf '{"gitSHA":"%s","environment":"main"}\n' "$BUILD_SHA" > docs/dist/deployment.json

FROM nginx:1.29-alpine AS runtime
ARG BUILD_SHA
LABEL org.opencontainers.image.revision=$BUILD_SHA
COPY deploy/docs/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/docs/dist /usr/share/nginx/html/docs
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=6 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/docs/deployment.json || exit 1
