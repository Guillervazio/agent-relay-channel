# El hub sin Windows. Nada del canal es de Windows: sólo lo era la única forma de
# alojarlo que estaba documentada, un servicio instalado desde una consola elevada.
#
#   docker build -t arc-hub .
#   docker run -d --name arc-hub -p 8765:8765 -v arc-data:/data \
#              -e ARC_TOKEN='<secreto>' arc-hub
#
# Una réplica, un volumen. P003 (docs/adr/P003-sqlite-on-a-file.md) asume un único
# proceso dueño del fichero, así que escalar esto a dos contenedores sobre el mismo
# volumen no es que esté sin probar: no está soportado.
#
# Sin ARC_TOKEN el hub no arranca, y dice por qué. Es deliberado: el canal acepta
# instrucciones entre agentes y no debe quedar escuchando sin autenticar.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero lo que fija versiones y reglas, que cambia poco: así la restauración se
# reaprovecha entre compilaciones. .editorconfig entra porque EnforceCodeStyleInBuild
# está activo — sin él la compilación de aquí no sería la misma que la de casa.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Arc.Core/Arc.Core.csproj src/Arc.Core/
COPY src/Arc.Hub/Arc.Hub.csproj src/Arc.Hub/
RUN dotnet restore src/Arc.Hub/Arc.Hub.csproj

COPY src/Arc.Core/ src/Arc.Core/
COPY src/Arc.Hub/ src/Arc.Hub/
RUN dotnet publish src/Arc.Hub/Arc.Hub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# El buzón vive en el volumen y no en la capa: recrear el contenedor no se lleva el
# canal por delante. El directorio se crea antes de bajar de privilegios, porque
# `app` no es root y no podría crearlo él.
RUN mkdir -p /data && chown app:app /data
VOLUME /data
USER app

ENV ARC_DB=/data/arc.db \
    ARC_URLS=http://0.0.0.0:8765 \
    ASPNETCORE_HTTP_PORTS=
EXPOSE 8765

ENTRYPOINT ["dotnet", "Arc.Hub.dll"]
