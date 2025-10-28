#!/bin/bash
# FileFlux Local Build Script (Unix/Linux/macOS)
# Builds and publishes FileFlux package to local NuGet feed

set -e

CONFIGURATION="${1:-Release}"
OUTPUT_PATH="D:/data/FileFlux/nupkg"

echo "==================================="
echo "FileFlux Local Build Script"
echo "==================================="
echo ""

# Change to FileFlux directory
cd "D:/data/FileFlux"

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore

# Build
echo "Building FileFlux..."
dotnet build -c "$CONFIGURATION" --no-restore

# Run tests
echo "Running tests..."
dotnet test -c "$CONFIGURATION" --no-build --verbosity minimal

# Pack
echo "Creating NuGet package..."
dotnet pack -c "$CONFIGURATION" --no-build -o "$OUTPUT_PATH"

# List created packages
echo ""
echo "==================================="
echo "Build Complete!"
echo "==================================="
echo ""
echo "Created packages:"
ls -lt "$OUTPUT_PATH"/FileFlux.*.nupkg | head -5

echo ""
echo "To use this package in FilerBasis:"
echo "  1. Update package reference in Filer.Basis.FluxIndex.csproj"
echo "  2. Run: dotnet restore"
echo ""
