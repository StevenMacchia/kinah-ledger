# widths of sample strings in WPF, for comparing a runner with a PC (CI diagnostic)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing
$g = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero); "system dpi: $($g.DpiX)"
$samples = '124.56M', 'Aerinwynne', 'Fire Temple · Conquest · Run 2 today', '전투력'
foreach ($fam in 'Segoe UI Variable Text', 'Segoe UI Variable Display', 'Segoe UI Variable Small', 'Segoe UI', 'Malgun Gothic') {
  foreach ($w in 'Normal', 'SemiBold') {
    foreach ($tfm in 'Ideal', 'Display') {
      $out = foreach ($s in $samples) {
        $tf = [System.Windows.Media.Typeface]::new([System.Windows.Media.FontFamily]::new($fam), [System.Windows.FontStyles]::Normal, [System.Windows.FontWeight]::FromOpenTypeWeight($(if ($w -eq 'SemiBold') { 600 } else { 400 })), [System.Windows.FontStretches]::Normal)
        $ft = [System.Windows.Media.FormattedText]::new($s, [System.Globalization.CultureInfo]::InvariantCulture, [System.Windows.FlowDirection]::LeftToRight, $tf, 12.0, [System.Windows.Media.Brushes]::White, 1.0)
        '{0:0.00}' -f $ft.WidthIncludingTrailingWhitespace
      }
      $gt = $null; $ok = [System.Windows.Media.Typeface]::new($fam).TryGetGlyphTypeface([ref]$gt)
      "{0,-26} {1,-9} {2,-8} {3}  glyphface={4}" -f $fam, $w, $tfm, ($out -join ' | '), $(if ($ok) { $gt.FamilyNames.Values -join '/' } else { 'none' })
    }
  }
}
