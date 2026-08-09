$j = Get-Content 'Assets\StreamingAssets\building_types.json' -Raw | ConvertFrom-Json
$j.legal_businesses | Where-Object { $_.id -match 'bak|din|rest|cafe' } | ForEach-Object {
    Write-Host "$($_.id): profit=$($_.inherent_profit) costs=$($_.inherent_costs) setup=$($_.setup_cost) ref=$($_.building_ref) populace=$($_.populace) cap=$($_.capacity) lv=$($_.lv_min)-$($_.lv_max)"
}
