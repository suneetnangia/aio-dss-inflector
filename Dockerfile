# Use the official .NET Core SDK image as the base image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory inside the container
WORKDIR /app

# Copy the project files to the container
COPY . .

# Build the project
RUN dotnet build Aio.Dss.Inflector.Svc/Aio.Dss.Inflector.Svc.csproj -c Release -o out
RUN dotnet test Aio.Dss.Inflector.Svc.Tests/Aio.Dss.Inflector.Svc.Tests.csproj

# Use the official .NET Core Runtime image as the base image
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime

# Set the working directory inside the container
WORKDIR /app

# Copy the published output from the build stage to the runtime stage
COPY --from=build /app/out .

# Set the entry point for the container
ENTRYPOINT ["dotnet", "Aio.Dss.Inflector.Svc.dll"]