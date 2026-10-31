# Dashboard (Next.js) production image — multi-stage, standalone output.
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# NEXT_PUBLIC_* are baked at build time (they ship in the client bundle).
ARG NEXT_PUBLIC_LOYALTY_API_BASE=http://localhost:8080
ARG NEXT_PUBLIC_LOYALTY_API_KEY=
ENV NEXT_PUBLIC_LOYALTY_API_BASE=$NEXT_PUBLIC_LOYALTY_API_BASE
ENV NEXT_PUBLIC_LOYALTY_API_KEY=$NEXT_PUBLIC_LOYALTY_API_KEY
RUN npm run build

FROM node:20-alpine AS run
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
EXPOSE 3000
CMD ["node", "server.js"]
