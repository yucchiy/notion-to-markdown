# Set the base image as the .NET 6.0 SDK (this includes the runtime)
FROM mcr.microsoft.com/dotnet/sdk:6.0 as build-env

# Copy everything and publish the release (publish implicitly restores and builds)
WORKDIR /app
COPY . ./
RUN dotnet publish ./notion-to-markdown.csproj -c Release -o out --no-self-contained

# Label the container
LABEL maintainer="Yuichiro Mukai <y.iky917@gmail.com>"
LABEL repository="https://github.com/yucchiy/notion-to-markdown"
LABEL homepage="https://github.com/yucchiy/notion-to-markdown"

# Label as GitHub action
LABEL com.github.actions.name="notion-to-markdown"
# Limit to 160 characters
LABEL com.github.actions.description="Custom action to export Notion database to local markdown files."
# See branding:
# https://docs.github.com/actions/creating-actions/metadata-syntax-for-github-actions#branding
LABEL com.github.actions.icon="activity"
LABEL com.github.actions.color="orange"

# Relayer the .NET SDK, anew with the build output
FROM mcr.microsoft.com/dotnet/sdk:6.0
COPY --from=build-env /app/out .
ENTRYPOINT [ "dotnet", "/notion-to-markdown.dll" ]