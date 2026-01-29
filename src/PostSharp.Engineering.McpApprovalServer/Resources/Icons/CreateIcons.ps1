# Script to create simple ICO files for the tray icon

function New-SimpleIco {
    param(
        [string]$Path,
        [byte]$R,
        [byte]$G,
        [byte]$B
    )

    $width = 16
    $height = 16
    $bpp = 32
    $pixelDataSize = $width * $height * 4
    $imageSize = $pixelDataSize + 40  # pixels + BITMAPINFOHEADER
    $offset = 6 + 16  # header + directory

    # ICO Header (6 bytes)
    $header = [byte[]]@(
        0, 0,       # Reserved (must be 0)
        1, 0,       # Type (1 = ICO)
        1, 0        # Number of images (1)
    )

    # Directory Entry (16 bytes)
    $directory = [byte[]]@(
        $width,           # Width
        $height,          # Height
        0,                # Color palette (0 = no palette)
        0,                # Reserved
        1, 0,             # Color planes
        $bpp, 0           # Bits per pixel
    )

    # Add image size (4 bytes, little-endian)
    $directory += [byte]($imageSize -band 0xFF)
    $directory += [byte](($imageSize -shr 8) -band 0xFF)
    $directory += [byte](($imageSize -shr 16) -band 0xFF)
    $directory += [byte](($imageSize -shr 24) -band 0xFF)

    # Add offset (4 bytes, little-endian)
    $directory += [byte]($offset -band 0xFF)
    $directory += [byte](($offset -shr 8) -band 0xFF)
    $directory += [byte](($offset -shr 16) -band 0xFF)
    $directory += [byte](($offset -shr 24) -band 0xFF)

    # BITMAPINFOHEADER (40 bytes)
    $bmpHeader = [byte[]]@(
        40, 0, 0, 0,      # Header size
        $width, 0, 0, 0,  # Width
        ($height * 2), 0, 0, 0,  # Height (doubled for ICO format - includes AND mask)
        1, 0,             # Planes
        $bpp, 0,          # Bits per pixel
        0, 0, 0, 0,       # Compression (none)
        0, 0, 0, 0,       # Image size (can be 0 for uncompressed)
        0, 0, 0, 0,       # X pixels per meter
        0, 0, 0, 0,       # Y pixels per meter
        0, 0, 0, 0,       # Colors used
        0, 0, 0, 0        # Important colors
    )

    # Pixel data (BGRA, bottom-up)
    $pixels = New-Object byte[] $pixelDataSize
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $i = ($y * $width + $x) * 4
            # Create a filled circle
            $cx = $x - 7.5
            $cy = $y - 7.5
            $dist = [Math]::Sqrt($cx * $cx + $cy * $cy)
            if ($dist -le 6.5) {
                $pixels[$i] = $B      # Blue
                $pixels[$i+1] = $G    # Green
                $pixels[$i+2] = $R    # Red
                $pixels[$i+3] = 255   # Alpha (opaque)
            } else {
                $pixels[$i] = 0
                $pixels[$i+1] = 0
                $pixels[$i+2] = 0
                $pixels[$i+3] = 0     # Alpha (transparent)
            }
        }
    }

    # Combine all parts
    $ico = $header + $directory + $bmpHeader + $pixels
    [System.IO.File]::WriteAllBytes($Path, $ico)
    Write-Host "Created: $Path"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Green icon for normal state (RGB: 76, 175, 80)
New-SimpleIco -Path (Join-Path $scriptDir "tray-normal.ico") -R 76 -G 175 -B 80

# Orange icon for pending state (RGB: 255, 152, 0)
New-SimpleIco -Path (Join-Path $scriptDir "tray-pending.ico") -R 255 -G 152 -B 0

Write-Host "Icons created successfully!"
