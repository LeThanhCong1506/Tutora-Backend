# Giai đoạn 1: Build aplication
# Sử dụng .NET SDK image để build code
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sao chép tất cả các file .csproj và thực hiện restore
# Điều này giúp tận dụng cache của Docker, chỉ restore lại khi có project mới hoặc thay đổi
COPY ["MV.PresentationLayer/MV.PresentationLayer.csproj", "MV.PresentationLayer/"]
COPY ["MV.ApplicationLayer/MV.ApplicationLayer.csproj", "MV.ApplicationLayer/"]
COPY ["MV.InfrastructureLayer/MV.InfrastructureLayer.csproj", "MV.InfrastructureLayer/"]
COPY ["MV.DomainLayer/MV.DomainLayer.csproj", "MV.DomainLayer/"]
RUN dotnet restore "MV.PresentationLayer/MV.PresentationLayer.csproj"

# Sao chép toàn bộ source code còn lại
COPY . .
WORKDIR "/src/MV.PresentationLayer"
RUN dotnet build "MV.PresentationLayer.csproj" -c Release -o /app/build

# Giai đoạn 2: Publish ứng dụng
FROM build AS publish
RUN dotnet publish "MV.PresentationLayer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn 3: Chạy ứng dụng
# Sử dụng ASP.NET runtime image, nhẹ hơn rất nhiều so với SDK image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MV.PresentationLayer.dll"]