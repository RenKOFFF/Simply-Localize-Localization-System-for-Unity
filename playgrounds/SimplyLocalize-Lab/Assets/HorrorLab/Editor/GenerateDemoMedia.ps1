Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Speech
$sourceRoot = Join-Path $PSScriptRoot '..'
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
  foreach ($code in @('en', 'ru', 'pl')) {
    $media = Join-Path $sourceRoot "Media/$code"
    New-Item -ItemType Directory -Force $media | Out-Null
    $content = (Get-Content -Raw -Encoding UTF8 (Join-Path $sourceRoot "Content/$code.json") | ConvertFrom-Json).translations
    foreach ($sign in @('Archive', 'Warning', 'Reception')) {
      $bitmap = New-Object System.Drawing.Bitmap 1024, 512
      $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
      $graphics.Clear([System.Drawing.Color]::FromArgb(29, 43, 42))
      $graphics.SmoothingMode = 'AntiAlias'
      $graphics.TextRenderingHint = 'AntiAliasGridFit'
      $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(224, 215, 181))
      $pen = New-Object System.Drawing.Pen $brush, 5
      $graphics.DrawRectangle($pen, 22, 22, 980, 468)
      $font = New-Object System.Drawing.Font 'Arial', 43, ([System.Drawing.FontStyle]::Bold)
      $small = New-Object System.Drawing.Font 'Arial', 19
      $format = New-Object System.Drawing.StringFormat
      $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
      $text = $content."World/Sign$sign" -replace '  /  ', "`n"
      $graphics.DrawString($text, $font, $brush, ([System.Drawing.RectangleF]::new(55, 90, 914, 300)), $format)
      $graphics.DrawString('WEST PRECINCT    /    1998    /    04', $small, $brush, ([System.Drawing.RectangleF]::new(55, 410, 914, 48)), $format)
      $bitmap.Save((Join-Path $media "$sign.png"), [System.Drawing.Imaging.ImageFormat]::Png)
      $format.Dispose(); $small.Dispose(); $font.Dispose(); $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    if ($code -eq 'pl') { continue }
    $speech.SelectVoice($(if ($code -eq 'ru') { 'Microsoft Irina Desktop' } else { 'Microsoft Zira Desktop' }))
    $speech.Rate = -1
    foreach ($id in @('01', '02', '03')) {
      $speech.SetOutputToWaveFile((Join-Path $media "$id.wav"))
      $speech.Speak($content."Dialogue/$id")
      $speech.SetOutputToNull()
    }
  }
} finally { $speech.Dispose() }
