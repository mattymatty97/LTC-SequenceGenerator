param(
    [Parameter(Mandatory=$false)]
    [string]$InputFile,
    
    [Parameter(Mandatory=$false)]
    [string]$FilesListPath
)

# Function to process a single file
function Process-MermaidFile {
    param (
        [string]$InputFile
    )
    
    # Validate that the input file exists
    if (-not (Test-Path -Path $InputFile)) {
        Write-Error "Input file not found: ${InputFile}"
        return $false
    }

    # Extract the base filename (without extension) and extension
    $baseFileName = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
    $extension = [System.IO.Path]::GetExtension($InputFile)
    $directory = [System.IO.Path]::GetDirectoryName($InputFile)

    # Check if the file has .mmd extension
    if ($extension -ne ".mmd") {
        Write-Error "Input file must have .mmd extension: ${InputFile}"
        return $false
    }

    # If directory is empty, use current directory
    if ([string]::IsNullOrWhiteSpace($directory)) {
        $directory = "."
    }

    # Construct the full paths
    $inputPath = $InputFile  # Use the original input path directly
    $outputPath = Join-Path -Path $directory -ChildPath "${baseFileName}.svg"

    # Run the mmdc command
    try {
        Write-Host "Converting ${inputPath} to ${outputPath}..."
        mmdc --configFile .\mermaidSettings.json -i $inputPath -o $outputPath
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Conversion successful! Output saved to: ${outputPath}" -ForegroundColor Green
            
            # Fix the SVG by replacing "stroke=none" with "stroke=black" using streaming approach
            Write-Host "Applying stroke fix to SVG file..." -ForegroundColor Yellow
            
            $tempFilePath = "${outputPath}.temp"
            $replacementCount = 0

            # Stream processing - read line by line
            $reader = [System.IO.StreamReader]::new($outputPath)
            $writer = [System.IO.StreamWriter]::new($tempFilePath)
            
            while (($line = $reader.ReadLine()) -ne $null) {
                $newLine = $line -replace 'stroke="none"', 'stroke="black"'
                
                # Count replacements
                if ($line -ne $newLine) {
                    $replacementCount += 1
                }
                
                $writer.WriteLine($newLine)
            }
            
            $reader.Close()
            $writer.Close()
            
            # Replace original with modified version
            Remove-Item -Path $outputPath
            Rename-Item -Path $tempFilePath -NewName $outputPath
            
            if ($replacementCount -gt 0) {
                Write-Host "Fixed SVG file. Replaced ${replacementCount} instance(s) of 'stroke=none' with 'stroke=black'" -ForegroundColor Green
            } else {
                Write-Host "No 'stroke=none' attributes found in the SVG file." -ForegroundColor Yellow
            }
            
            return $true
        } else {
            Write-Error "mmdc command failed with exit code: ${LASTEXITCODE}"
            return $false
        }
    } catch {
        Write-Error "An error occurred: $_"
        return $false
    }
}

# Check if we're processing from a file list (from batch file)
if ($FilesListPath -and (Test-Path $FilesListPath)) {
    $filesToProcess = Get-Content $FilesListPath | ForEach-Object { $_ -replace '"', '' }
    $processedCount = 0
    $totalFiles = $filesToProcess.Count
    
    foreach ($file in $filesToProcess) {
        $fileNumber = $processedCount + 1
        # Use format operator instead of string interpolation
        Write-Host ("`n=== Processing file {0} of {1}: {2} ===" -f $fileNumber, $totalFiles, $file) -ForegroundColor Cyan
        if (Process-MermaidFile -InputFile $file.TrimEnd()) {
            $processedCount++
        }
    }
    
    # Use format operator instead of string interpolation
    Write-Host ("`nProcessed {0} out of {1} files successfully." -f $processedCount, $totalFiles) -ForegroundColor Cyan
}
# Check if we have a single input file
elseif ($InputFile) {
    Process-MermaidFile -InputFile $InputFile.TrimEnd()
}
# If no input method was specified, show usage
else {
    Write-Host "Mermaid to SVG Converter" -ForegroundColor Cyan
    Write-Host "----------------------" -ForegroundColor Cyan
    Write-Host "Usage options:" -ForegroundColor White
    Write-Host "1. Drag and drop .mmd file(s) onto ConvertMermaid.bat" -ForegroundColor White
    Write-Host "2. Run via command line: .\ConvertMermaid.ps1 -InputFile path\to\file.mmd" -ForegroundColor White
}