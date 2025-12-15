# Dockerfile con errores intencionados para tests
FROM nonexistent-base-image:invalid-tag

WORKDIR /app

# Esto fallará porque la imagen base no existe
COPY . .

RUN dotnet build
