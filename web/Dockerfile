# Production image for the compose deployment (S13). `aspire publish` builds it; `ng serve` is unaffected.
FROM node:26-slim AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.28-alpine
# The official image renders /etc/nginx/templates/*.template with the container's environment (PORT, services__api__http__0).
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
