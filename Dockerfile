FROM mcr.microsoft.com/dotnet/aspnet:6.0

# Copy
WORKDIR /app
COPY artifacts/Enterprise.5.0.0.linux-x64/ ./

# Expose
EXPOSE 80

# Install wkhtmltopdf
RUN apt update &&\
	apt -y install wget &&\
	wget https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6-1/wkhtmltox_0.12.6-1.buster_amd64.deb &&\ 
	apt -y install ./wkhtmltox_0.12.6-1.buster_amd64.deb &&\
	rm ./wkhtmltox_0.12.6-1.buster_amd64.deb

ENTRYPOINT ["./Smartstore.Web", "--urls", "http://0.0.0.0:80"]
