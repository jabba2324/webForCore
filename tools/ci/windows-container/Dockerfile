# webForCore in a Windows container (CI's windows-container job, tools/ci/windows_container.cs): webforcore-host and an
# application in Microsoft's ASP.NET Core runtime image, as a deployment would build it. The context holds host/
# (webforcore-host's build output) and app/ (the application's folder, with its bin_dotnet). No SDK in the image: the
# application's pages and views are compiled at run time by webForCore's own compilers.
ARG BASE=mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-ltsc2025
FROM ${BASE}
COPY host/ C:/host/
COPY app/ C:/app/
WORKDIR C:/app
ENV Kestrel__Endpoints__Http__Url=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "C:\\host\\webforcore-host.dll", "--", "bin_dotnet\\webForCore.Mvc.Sample.dll"]
