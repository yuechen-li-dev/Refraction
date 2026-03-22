FROM node:20-alpine AS build
WORKDIR /app

COPY frontend/Refraction.App/package.json frontend/Refraction.App/package-lock.json ./
RUN npm ci

COPY frontend/Refraction.App/. .

ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

FROM caddy:2.10-alpine AS final
WORKDIR /srv

COPY frontend/Refraction.App/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv

EXPOSE 80
EXPOSE 443
