FROM node:24-alpine AS build
WORKDIR /source

ARG STU_WEB_WORKSPACE
COPY package.json package-lock.json ./
COPY src/web/stu-app/package.json src/web/stu-app/package.json
COPY src/web/stu-admin/package.json src/web/stu-admin/package.json
RUN npm ci

COPY src/web/ src/web/
RUN npm run build -w ${STU_WEB_WORKSPACE} && cp -R src/web/${STU_WEB_WORKSPACE}/dist /web-dist

FROM nginx:1.29-alpine-slim AS runtime
RUN apk upgrade --no-cache
COPY infra/web/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web-dist /usr/share/nginx/html
RUN worker="$(find /usr/share/nginx/html/assets -maxdepth 1 -type f -name 'maplibre-gl-worker-*.js' -print -quit)" \
    && test -n "$worker" \
    && ln -s "$(basename "$worker")" /usr/share/nginx/html/assets/maplibre-gl-worker.mjs
EXPOSE 80
