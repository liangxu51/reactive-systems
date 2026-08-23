FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the main project's .csproj first (not the test project - it's
# never published) so `dotnet restore` is cached as its own layer and isn't
# invalidated by unrelated source edits.
COPY OrderService.Api.csproj ./
RUN dotnet restore OrderService.Api.csproj

# Now bring in the rest of the source and publish. The test project
# (order-service-cs.Tests/) is copied along with everything else here, but
# OrderService.Api.csproj already excludes its files from compilation (see
# the Compile Remove/Content Remove/None Remove items in the .csproj), and
# publishing OrderService.Api.csproj specifically - not a solution file -
# means the test project is never built or included in the output.
COPY . .
RUN dotnet publish OrderService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderService.Api.dll"]
