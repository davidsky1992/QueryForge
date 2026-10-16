# syntax=docker/dockerfile:1
#
# Multi-stage build for the QueryForge React (Vite) frontend.
# Build context: src/QueryForge.Web
#
# The image serves the static bundle with nginx and reverse-proxies /api and /health to the API, so the browser
# only talks to one origin. VITE_API_BASE_URL is empty by default, which makes the app call relative URLs.
# VITE_* values are compiled into the public JavaScript bundle: never pass secrets as build arguments.

# ---------------------------------------------------------------------------
# Build the static bundle
# ---------------------------------------------------------------------------
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
RUN npm run build

# ---------------------------------------------------------------------------
# Serve with non-root nginx (listens on 8080)
# ---------------------------------------------------------------------------
FROM nginxinc/nginx-unprivileged:1.27-alpine AS web

# Rendered to /etc/nginx/conf.d/default.conf at startup; only variables defined in the environment are substituted.
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# Where nginx forwards API requests, and the DNS resolver used to look it up at request time
# (127.0.0.11 is Docker's embedded DNS server).
ENV API_UPSTREAM=http://api:8080 \
    NGINX_RESOLVER=127.0.0.11

EXPOSE 8080
