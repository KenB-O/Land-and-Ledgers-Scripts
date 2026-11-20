param(
    [Parameter(Mandatory = $true)][string]$SavePath,
    [string]$OutputPath
)

# Read-only diagnostics over a production SaveLoadManager JSON snapshot.
# Numeric status groups retain their serialized meanings; unknown metrics
# remain explicit rather than being inferred from professions or scenery.
if ($OutputPath -and [string]::Equals([IO.Path]::GetFullPath($SavePath), [IO.Path]::GetFullPath($OutputPath), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The report output must not overwrite its source save.'
}
$save = Get-Content -LiteralPath $SavePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
$residents = @($save.population.people | Where-Object {
    -not $_.isOffMap -and $_.deathDayIndex -lt 0 -and $_.householdId -ge 0
})
$residentIds = @($residents | ForEach-Object id)
$businesses = @($save.businesses.sharedBusinesses | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_.instanceId) })
$playerBusiness = $save.businesses.deepGeneralStore.currentBusiness
if ($playerBusiness -and -not [string]::IsNullOrWhiteSpace($playerBusiness.instanceId) -and $playerBusiness.instanceId -notin @($businesses.instanceId)) {
    $businesses += $playerBusiness
}
$tasks = @($save.systems.tasks.tasks | Where-Object { $_ })
$workingPeople = @($tasks | Where-Object {
    $_.status -eq 2 -and $_.assigneeId.Id -in $residentIds
} | ForEach-Object { $_.assigneeId.Id } | Sort-Object -Unique)
$lots = @($businesses | ForEach-Object { $_.runtime.retailItemLots } | Where-Object {
    $_ -and $_.quantityUnits -gt 0
})
$meals = @($save.population.mealRecords | Where-Object { $_ })
$missed = @($save.population.missedMeals | Where-Object { $_ })
$duplicateMealKeys = @((@($meals) + @($missed)) | Group-Object PersonId,DayIndex,Slot |
    Where-Object Count -gt 1)
$householdCash = @($save.population.householdLedgers | ForEach-Object {
    $entries = @($_.Entries | Where-Object { $_ })
    if ($entries.Count -gt 0) { [long]$entries[-1].BalanceAfterCents } else { 0L }
})
$retailPayments = @($save.population.householdLedgers | ForEach-Object { $_.Entries } |
    Where-Object { $_.AmountCents -lt 0 -and $_.Purpose -like 'shopping:*' })
$report = [ordered]@{
    day = $save.time.absoluteDayIndex
    registeredPeople = @($save.population.people).Count
    localLivingResidents = $residents.Count
    residentsWithWorkplaces = @($residents | Where-Object workplaceBuildingId -ge 0).Count
    formalEmploymentRecords = @($save.systems.employments.relationships | Where-Object { $_ }).Count
    residentsWorking = $workingPeople.Count
    taskCountsByStatus = @($tasks | Group-Object status | ForEach-Object {
        [ordered]@{ status = [int]$_.Name; count = $_.Count }
    })
    mealsEaten = $meals.Count
    mealsMissed = $missed.Count
    duplicateMealKeys = $duplicateMealKeys.Count
    householdsWithBreadOrFlour = @($save.population.householdInventories | Where-Object {
        @($_.Lots | Where-Object { $_.ItemId -in @('bread','flour') -and $_.QuantityUnits -gt $_.HeldUnits }).Count -gt 0
    }).Count
    openNeedsByItem = @($save.population.purchasingNeeds | Where-Object { $_.Status -eq 0 -and $_.UnitsNeeded -gt 0 } |
        Group-Object ItemId | ForEach-Object { [ordered]@{ item = $_.Name; households = @($_.Group.HouseholdId | Sort-Object -Unique).Count } })
    businesses = @($businesses | ForEach-Object { [ordered]@{
        id = $_.instanceId; cashCents = $_.runtime.currentCashCents
        recordedRetailCostCents = $_.runtime.weekToDateRetailCostOfGoodsSoldCents
        uncostedSoldUnits = $_.runtime.weekToDateUncostedRetailUnitsSold
        receiptSequence = $_.runtime.nextRetailReceiptSequence
    } })
    totalHouseholdCashCents = ($householdCash | Measure-Object -Sum).Sum
    totalBusinessCashCents = ($businesses.runtime.currentCashCents | Measure-Object -Sum).Sum
    householdRetailPaymentRecords = $retailPayments.Count
    householdRetailSpendCents = -($retailPayments.AmountCents | Measure-Object -Sum).Sum
    consumedFoodByItem = @($meals | ForEach-Object { $_.ItemsConsumed } | Group-Object ItemId | ForEach-Object {
        [ordered]@{ item = $_.Name; inventoryUnits = ($_.Group.QuantityUnits | Measure-Object -Sum).Sum }
    })
    retailInventoryByItem = @($lots | Group-Object itemId | ForEach-Object {
        [ordered]@{ item = $_.Name; units = ($_.Group.quantityUnits | Measure-Object -Sum).Sum }
    })
    namedSupplierOrdersByStage = @($save.passXlSlice.procurement.Orders | Where-Object { $_ } | Group-Object Stage |
        ForEach-Object { [ordered]@{ stage = [int]$_.Name; count = $_.Count } })
    freightShipments = @($save.passXlSlice.series2Pass9.Freight.Shipments | ForEach-Object {
        [ordered]@{ id = $_.shipmentId; state = $_.state; delivered = $_.deliveryApplied; feeCollected = $_.freightRevenueCollected; transportBound = $_.physicalTransportBound }
    })
    registeredParcels = @($save.systems.parcels.parcels | Where-Object { $_ }).Count
    constructionProjects = @($save.systems.constructionProjects.Projects | Where-Object { $_ }).Count
    limits = @('Snapshot only; no simulation advanced or save modified.',
        'Formal employment, assigned workplaces and actual work are distinct.',
        'Bread/flour coverage is not a complete food or prepared-meal audit.',
        'Cash totals alone do not prove conservation across external trade, expenses or wages.',
        'Resting, task-stall causes, expenses, failed transactions and runtime exceptions require schedules and bounded Console evidence; not inferred here.')
}
$json = $report | ConvertTo-Json -Depth 8
if ($OutputPath) { [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false)) }
$json
