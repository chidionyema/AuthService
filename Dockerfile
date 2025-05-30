# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
VOLUME /app/keys

WORKDIR /src

# Copy project file and restore dependencies
COPY ["src/haworks.csproj", "./"]
RUN dotnet --version && dotnet restore "./haworks.csproj"

# Copy the rest of the source code
COPY src/. .

# Build the project
RUN dotnet build "haworks.csproj" -c Release -o /app/build

# Publish the application to a folder for runtime
RUN dotnet publish "haworks.csproj" -c Release -o /app/publish

# Use the ASP.NET runtime image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published output from the build stage
COPY --from=build /app/publish .

# Copy the entrypoint script into the image
COPY /scripts/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Set the entrypoint to the script instead of directly launching dotnet haworks.dll
ENTRYPOINT ["/app/entrypoint.sh"]