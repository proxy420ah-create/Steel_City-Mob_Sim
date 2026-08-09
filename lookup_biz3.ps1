$j = Get-Content 'Assets\StreamingAssets\building_types.json' -Raw | ConvertFrom-Json
Write-Host "=== Apartment-related ==="
$j.legal_businesses | Where-Object { $_.id -match 'apart|tenem|hotel|lodg' } | ForEach-Object {
    Write-Host "$($_.id): profit=$($_.inherent_profit) costs=$($_.inherent_costs) setup=$($_.setup_cost) ref=$($_.building_ref) size=$($_.size) layout=$($_.size_layout) populace=$($_.populace) cap=$($_.capacity) lv=$($_.lv_min)-$($_.lv_max)"
}
Write-Host ""
Write-Host "=== Diner-like (small food) ==="
$j.legal_businesses | Where-Object { $_.id -match 'din|lunch|food|eat' } | ForEach-Object {
    Write-Host "$($_.id): profit=$($_.inherent_profit) costs=$($_.inherent_costs) setup=$($_.setup_cost) ref=$($_.building_ref) populace=$($_.populace) cap=$($_.capacity)"
}
