# How to run

- `dotnet run --project TestFileWriting --framework=net5.0 -- --memorymaps=true --minsize=1B --maxsize=10MB`
- `dotnet run --project TestFileWriting --framework=net5.0 -- --filestreams=true  --minsize=1B --maxsize=10MB`

## Docker
- `docker build --rm -t test .`
- `docker.exe run -it --rm test -- --memorymaps=true --minsize=1B --maxsize=10MB`
- `docker.exe run -it --rm test -- --filestreams=true --minsize=1B --maxsize=10MB`

## Example results:
https://github.com/marcin-krystianc/nuget_11031/blob/master/TestFileWriting/FIleWritingResults.txt
