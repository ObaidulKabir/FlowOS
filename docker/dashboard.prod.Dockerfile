FROM node:20-alpine AS build

WORKDIR /app

COPY apps/dashboard/package*.json ./
RUN npm ci

COPY apps/dashboard/ .

ARG FLOWOS_BUILD=0
ARG VITE_FLOWOS_BUILD=0
ENV FLOWOS_BUILD=$FLOWOS_BUILD
ENV VITE_FLOWOS_BUILD=$VITE_FLOWOS_BUILD

RUN npm run build

FROM nginx:1.27-alpine AS final

COPY docker/nginx/dashboard.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
