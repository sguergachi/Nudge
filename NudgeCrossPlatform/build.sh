#!/bin/bash
# Build Nudge - Single file compilation
# Jon Blow style: No projects, no ceremony, just compile the code

set -e

echo "=== Building Nudge ==="

# Check if we have mono/csc or dotnet
if command -v csc &> /dev/null; then
    echo "Using Mono C# compiler..."
    csc -out:nudge nudge.cs -r:System.Net.Sockets.dll
    csc -out:nudge-notify nudge-notify.cs
elif command -v dotnet &> /dev/null; then
    echo "Using dotnet..."
    # Create minimal single-file projects on the fly
    cat > nudge.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Nudge</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge.cs" />
  </ItemGroup>
</Project>
EOF

    cat > nudge-notify.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>NudgeNotify</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge-notify.cs" />
  </ItemGroup>
</Project>
EOF

    dotnet build nudge.csproj -c Release
    dotnet build nudge-notify.csproj -c Release

    # Copy binaries to root
    cp bin/Release/net8.0/nudge ./ || true
    cp bin/Release/net8.0/nudge-notify ./ || true

    echo "✓ Built successfully"
else
    echo "ERROR: No C# compiler found!"
    echo "Install mono or dotnet SDK"
    exit 1
fi

echo ""
echo "✓ Build complete"
echo ""
echo "Run: ./nudge [csv-path]"
echo "Notify: ./nudge-notify YES|NO"
