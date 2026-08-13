FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

COPY ["MV.PresentationLayer/MV.PresentationLayer.csproj", "MV.PresentationLayer/"]
COPY ["MV.ApplicationLayer/MV.ApplicationLayer.csproj", "MV.ApplicationLayer/"]
COPY ["MV.InfrastructureLayer/MV.InfrastructureLayer.csproj", "MV.InfrastructureLayer/"]
COPY ["MV.DomainLayer/MV.DomainLayer.csproj", "MV.DomainLayer/"]

RUN dotnet restore "MV.PresentationLayer/MV.PresentationLayer.csproj" --nologo

COPY . .
WORKDIR "/src/MV.PresentationLayer"

RUN dotnet publish "MV.PresentationLayer.csproj" -c Release -o /app/publish \
    --no-restore /p:UseAppHost=false \
    && rm -rf /root/.nuget/packages

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
# ffmpeg: tách audio khỏi video buổi học trước khi gửi Gemini (ClassSessionVideoAiService) —
# FFMpegCore chỉ là wrapper, cần binary ffmpeg thật có sẵn trên máy chạy.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=restore /app/publish .
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet MV.PresentationLayer.dll"]