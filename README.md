This is a mimimal test server implementing enough of the Docker V2 HTTP API to support a `docker push` and `docker pull`.

* Start with `dotnet run`.
* Pull an image with `docker pull alpine`.
* Retag the image with `docker tag alpine 10.1.1.1:5001/alpine` (where 10.1.1.1 is the IP address where the application is exposed).
* Push the image with `docker push 10.1.1.1:5001/alpine`
* Pull the image with `docker pull 10.1.1.1:5001/alpine`
