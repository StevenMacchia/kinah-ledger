# the meter's figure: a TextBlock-sized column (Ideal) against the Odometer's cells (Display, per DPI) (CI diagnostic)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$tf = [System.Windows.Media.Typeface]::new([System.Windows.Media.FontFamily]::new('Segoe UI Variable Text, Segoe UI'), [System.Windows.FontStyles]::Normal, [System.Windows.FontWeight]::FromOpenTypeWeight(600), [System.Windows.FontStretches]::Normal)
function W($s, $size, $mode, $dip) { [System.Windows.Media.FormattedText]::new($s, [System.Globalization.CultureInfo]::InvariantCulture, [System.Windows.FlowDirection]::LeftToRight, $tf, $size, [System.Windows.Media.Brushes]::White, $null, $mode, $dip).WidthIncludingTrailingWhitespace }
foreach ($size in 12.5, 14.5) {
  $ideal = W '888.88M' $size ([System.Windows.Media.TextFormattingMode]::Ideal) 1.0
  foreach ($dip in 1.0, 1.25, 1.5, 2.0) {
    $dw = 0; foreach ($c in '0123456789'.ToCharArray()) { $dw = [Math]::Max($dw, (W "$c" $size ([System.Windows.Media.TextFormattingMode]::Display) $dip)) }
    $odo = [Math]::Ceiling(5 * $dw + (W '.' $size ([System.Windows.Media.TextFormattingMode]::Display) $dip) + (W 'M' $size ([System.Windows.Media.TextFormattingMode]::Display) $dip))
    "size {0} dip {1}: column (Ideal 888.88M) {2:0.00} -> {3}; odometer cells {4}  {5}" -f $size, $dip, $ideal, [Math]::Ceiling($ideal), $odo, $(if ($odo -gt [Math]::Ceiling($ideal) + 0.75) { 'CUT' } else { '' })
  }
}
