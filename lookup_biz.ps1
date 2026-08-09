$j = Get-Content 'Assets\StreamingAssets\building_types.json' -Raw | ConvertFrom-Json
$ids = @('butchers','bakers','diner','barbers')
foreach ($id in $ids) {
    $b = $j.legal_businesses | Where-Object { $_.id -eq $id }
    if ($b) {
        Write-Host "=== $($b.id) ==="
        Write-Host "  name=$($b.name) group=$($b.group) size=$($b.size) size_layout=$($b.size_layout)"
        Write-Host "  profit_group=$($b.profit_group) running_cost_group=$($b.running_cost_group)"
        Write-Host "  inherent_profit=$($b.inherent_profit) inherent_costs=$($b.inherent_costs)"
        Write-Host "  setup_cost=$($b.setup_cost) setup_time=$($b.setup_time)"
        Write-Host "  building_ref=$($b.building_ref) populace=$($b.populace) capacity=$($b.capacity)"
        Write-Host "  protection=$($b.protection) lv_min=$($b.lv_min) lv_max=$($b.lv_max)"
        Write-Host "  stock_value=$($b.stock_value) clothes=$($b.clothes)"
        Write-Host ""
    } else {
        Write-Host "NOT FOUND: $id"
    }
}
