# How to run

- `dotnet run --project TestFileWriting --framework=net5.0 -- --memorymaps=true`
- `dotnet run --project TestFileWriting --framework=net5.0 -- --filestreams=true`

## Docker
- `docker build --rm -t test .`
- `docker.exe run -it --rm test -- --memorymaps=true`
- `docker.exe run -it --rm test -- --filestreams=true`
