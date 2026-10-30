# syntax=docker/dockerfile:1
# Multi-stage build for the Loyalty API and Worker. Pass --build-arg PROJECT to pick one.
ARG PROJECT=Loyalty.Api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV DLL=${PROJECT}.dll
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet $DLL"]
