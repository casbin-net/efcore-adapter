# EFCore-Adapter

## How to use

1. Create a webapi starter project or you can directly use my demo project

    ```shell
    git clone https://github.com/xcaptain/CasbinTestProj
    ```

2. Clone this library (when this package get published this step would omitted)

    ```shell
    git clone https://github.com/casbin-net/EFCore-Adapter
    ```

3. Create a local nuget package

    ```shell
    cd EFCore-Adapter
    dotnet pack
    ```

    then you can see package file at `bin/Debug/`

4. Modify the csproj file at `CasbinTestProj` to find the location of `EFCore-Adapter`, e.g.

    ```text
    <RestoreSources>$(RestoreSources);~/github/EFCore-Adapter/bin/Debug/</RestoreSources>
   ```

5. run the demo project to see how to get roles of a user
