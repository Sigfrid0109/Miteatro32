param([switch]$MySql,[string]$Configuration="Debug")
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$script:assemblyFolder=Join-Path $pwd ('bin/'+$Configuration)
$script:resolver=[ResolveEventHandler]{param($sender,$eventArgs) $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name; $candidate=Join-Path $script:assemblyFolder ($name+'.dll'); if(Test-Path $candidate){return [Reflection.Assembly]::LoadFrom($candidate)}; return $null}
[AppDomain]::CurrentDomain.add_AssemblyResolve($script:resolver)
[Reflection.Assembly]::LoadFrom((Join-Path $pwd ('bin/'+$Configuration+'/Miteatro.exe'))) | Out-Null
$testRoot=Join-Path $pwd 'test-output'
New-Item -ItemType Directory -Force $testRoot | Out-Null
$testPath=Join-Path $testRoot ('test-'+[guid]::NewGuid().ToString()+'.xml')
if($MySql){
 $dbName='test_'+[guid]::NewGuid().ToString('N')
 & 'C:/Program Files/MySQL/MySQL Server 9.4/bin/mysql.exe' --no-defaults --host=127.0.0.1 --port=33317 --user=root --execute="CREATE DATABASE $dbName CHARACTER SET utf8mb4;"
 if($LASTEXITCODE -ne 0){throw 'No se creó base aislada de prueba'}
 $repo=New-Object Miteatro.MySqlRepository("Server=127.0.0.1;Port=33317;Database=$dbName;User Id=root;SslMode=Required;")
}else{$repo=New-Object Miteatro.LocalRepository($testPath)}
$svc=New-Object Miteatro.TheaterService($repo)
$script:checks=0
function Assert($condition,$name){if(!$condition){throw "FAIL: $name"};$script:checks++;Write-Output "PASS: $name"}
function Reject([scriptblock]$operation,$name){$rejected=$false;try{& $operation}catch{$rejected=$true};Assert $rejected $name}
$svc.Setup('Administrador','admin','PruebaSegura123!')
$svc.Login('admin','PruebaSegura123!') | Out-Null
$admin=$svc.Current.Id
$u=New-Object Miteatro.User
$u.Name='Cajero';$u.Login='caja';$u.Role=[Miteatro.Role]::Cajero
$svc.SaveUser($u,'OtraSegura123!')
$p=New-Object Miteatro.Performance
$p.Name='Función';$p.Starts=[datetime]::Now.AddDays(1);$p.Room='Principal';$p.Capacity=10;$p.Adult=100;$p.Child=60;$p.Senior=80
$svc.SavePerformance($p)
$product=New-Object Miteatro.Product
$product.Name='Agua';$product.Price=20;$product.Minimum=2
$svc.SaveProduct($product);$svc.AdjustStock($product.Id,10,'Compra')
$promo=New-Object Miteatro.Promotion
$promo.Code='DOS';$promo.Kind='2x1';$promo.From=[datetime]::Today;$promo.Until=[datetime]::Today.AddDays(1)
$svc.SavePromotion($promo)
$svc.Logout();$svc.Login('caja','OtraSegura123!')|Out-Null
Reject {$svc.SaveProduct($product)} 'Cajero no modifica catálogo'
Reject {$svc.OpenShift(-1)} 'Fondo negativo rechazado'
$svc.OpenShift(200)
$cart=New-Object 'System.Collections.Generic.List[Miteatro.SaleLine]'
$cart.Add($svc.TicketLine($p.Id,'Adulto',2));$cart.Add($svc.ProductLine($product.Id,3))
Assert (($svc.Quote($cart,'DOS') | Measure-Object Total -Sum).Sum -eq 160) '2x1 aplicado a boletos'
Reject {$svc.Checkout($cart,'','Tarjeta',0,'',$null,'bad')} 'Tarjeta requiere referencia'
$sale=$svc.Checkout($cart,'','Mixto',100,'AUT-001',$null,'request-1')
Assert ($sale.Total -eq 260 -and $sale.Cash -eq 100 -and $sale.Card -eq 160) 'Pago mixto correcto'
Assert ($svc.Available($p.Id) -eq 8 -and $svc.Data.Products[0].Stock -eq 7) 'Cupo e inventario actualizados'
$repeat=$svc.Checkout($cart,'','Mixto',100,'AUT-001',$null,'request-1')
Assert ($svc.Data.Sales.Count -eq 1) 'Solicitud repetida no duplica venta'
Reject {$svc.Return($sale.Id,$sale.Lines[0].Id,1,$false,$true,'Motivo','REF',$admin)} 'No acepta autorización falsificada'
$approver=$svc.Authorize('admin','PruebaSegura123!')
$svc.Return($sale.Id,$sale.Lines[0].Id,1,$false,$true,'Cambio de planes','REF-001',$approver)
Assert ($svc.Available($p.Id) -eq 9) 'Devolución libera cupo'
Assert ($svc.Data.Refunds[0].Amount -eq 100 -and $svc.Data.Refunds[0].Cash -eq 38.46 -and $svc.Data.Refunds[0].Card -eq 61.54) 'Reembolso mixto proporcional'
Reject {$svc.Return($sale.Id,$sale.Lines[0].Id,2,$false,$true,'Motivo','REF',$svc.Authorize('admin','PruebaSegura123!'))} 'Impide devolver más de lo comprado'
$approver=$svc.Authorize('admin','PruebaSegura123!')
$svc.Return($sale.Id,$null,0,$true,$true,'Cancelación total','REF-002',$approver)
Assert (($svc.Data.Refunds | Measure-Object Cash -Sum).Sum -eq 100 -and ($svc.Data.Refunds | Measure-Object Card -Sum).Sum -eq 160) 'Reembolso completo cuadra al centavo'
Assert ($svc.Available($p.Id) -eq 10 -and $svc.Data.Products[0].Stock -eq 10) 'Cancelación restaura inventario y cupo'
Assert ($svc.Expected($svc.Active.Id) -eq 200) 'Caja considera reembolsos'
$svc.MoveCash(-50,'Retiro autorizado',$svc.Authorize('admin','PruebaSegura123!'))
Assert ($svc.Expected($svc.Active.Id) -eq 150) 'Retiro afecta caja'
$svc.CloseShift(148)
Assert ($svc.Data.Shifts[0].Difference -eq -2) 'Corte calcula faltante'
$loaded=New-Object Miteatro.TheaterService($repo)
Assert ($loaded.Data.Sales.Count -eq 1 -and $loaded.Data.Refunds.Count -eq 3) 'Persistencia completa'
$svc.Logout();$svc.Login('admin','PruebaSegura123!')|Out-Null
$backup=Join-Path $testRoot ('backup-'+[guid]::NewGuid().ToString()+'.xml')
$svc.Backup($backup)
$svc.Restore($backup)
Assert ($null -eq $svc.Current -and $svc.Data.Sales.Count -eq 1) 'Restauración exige nuevo acceso'
$svc.Login('admin','PruebaSegura123!')|Out-Null
$stale=New-Object Miteatro.TheaterService($repo)
$before=$stale.Data.Revision
$svc.AdjustStock($product.Id,1,'Nueva compra')
Reject {$stale.Login('admin','PruebaSegura123!')} 'Conflicto de revisión impide sobrescribir datos'
Assert ($stale.Data.Revision -eq $before) 'Fallo de persistencia revierte memoria'
Reject {$svc.Login('admin','incorrecta')} 'Contraseña incorrecta rechazada'
$svc.Login('admin','PruebaSegura123!')|Out-Null
$svc.OpenShift(0)
$cart.Clear();$cart.Add($svc.TicketLine($p.Id,'Adulto',6));$cart.Add($svc.TicketLine($p.Id,'Niño',6))
Reject {$svc.Quote($cart,'')} 'Cupo se verifica sobre carrito completo'
$cart.Clear();$cart.Add($svc.TicketLine($p.Id,'Adulto',1))
$expired=New-Object Miteatro.Promotion
$expired.Code='VENCIDO';$expired.From=[datetime]::Today.AddDays(-2);$expired.Until=[datetime]::Today.AddDays(-1);$expired.Value=10
$svc.SavePromotion($expired)
Reject {$svc.Quote($cart,'VENCIDO')} 'Promoción vencida rechazada'
$svc.CloseShift(0)
# Additional business-rule and recovery cases.
$svc.OpenShift(200)
$cart.Clear();$cart.Add($svc.TicketLine($p.Id,'Adulto',1));$cart.Add($svc.TicketLine($p.Id,'Adulto',1))
Assert (($svc.Quote($cart,'DOS') | Measure-Object Total -Sum).Sum -eq 100) '2x1 agrupa líneas separadas de la misma tarifa'
$settings=$svc.Data.Settings
$settings.DiscountApproval=10
$settings.BackupFolder=Join-Path $testRoot ('automatic-'+[guid]::NewGuid().ToString('N'))
$svc.SaveSettings($settings)
$svc.Logout();$svc.Login('caja','OtraSegura123!')|Out-Null
Reject {$svc.TicketLine($p.Id,'Adulto',1)} 'Otro cajero no opera el turno ajeno'
$svc.Logout();$svc.Login('admin','PruebaSegura123!')|Out-Null
$svc.CloseShift(200)
$svc.Logout();$svc.Login('caja','OtraSegura123!')|Out-Null
$svc.OpenShift(200)
Reject {$svc.Checkout($cart,'DOS','Efectivo',100,'',$null,'discount-rejected')} 'Descuento superior al umbral requiere autorización'
$discountSale=$svc.Checkout($cart,'DOS','Efectivo',100,'',$svc.Authorize('admin','PruebaSegura123!'),'discount-approved')
Assert ($discountSale.Total -eq 100) 'Descuento autorizado queda registrado'
$cart.Clear();$cart.Add($svc.ProductLine($product.Id,1))
$productSale=$svc.Checkout($cart,'','Efectivo',20,'',$null,'product-refund')
$stockBeforeReturn=$svc.Data.Products[0].Stock
$svc.Return($productSale.Id,$productSale.Lines[0].Id,1,$false,$false,'Producto abierto','',$svc.Authorize('admin','PruebaSegura123!'))
Assert ($svc.Data.Products[0].Stock -eq $stockBeforeReturn) 'Producto no revendible no vuelve al stock'
$svc.CloseShift($svc.Expected($svc.Active.Id))
$svc.Logout();$svc.Login('admin','PruebaSegura123!')|Out-Null
$product.Price=25
$svc.SaveProduct($product)
Assert ($svc.Data.Sales.Find([Predicate[Miteatro.Sale]]{param($x) $x.Id -eq $productSale.Id}).Lines[0].UnitPrice -eq 20) 'Cambiar catálogo conserva precios históricos'
$p.Capacity=1
Reject {$svc.SavePerformance($p)} 'No permite reducir el cupo bajo los boletos vigentes'
$p.Capacity=10
$autoPath=Join-Path $settings.BackupFolder ('MiTeatro-'+[datetime]::Today.ToString('yyyy-MM-dd')+'.xml')
Assert (Test-Path $autoPath) 'Respaldo automático creado'
$beforeRevision=$svc.Data.Revision
$badXml=[IO.File]::ReadAllText($autoPath).Replace('<Stock>10</Stock>','<Stock>-1</Stock>')
$badPath=Join-Path $testRoot ('invalid-'+[guid]::NewGuid().ToString('N')+'.xml')
[IO.File]::WriteAllText($badPath,$badXml)
Reject {$svc.Restore($badPath)} 'Respaldo inconsistente rechazado'
Assert ($svc.Data.Revision -eq $beforeRevision) 'Restauración inválida no altera datos'
$svc.PrintRecorded($discountSale.Id,$false)
$svc.PrintRecorded($discountSale.Id,$true)
$printedSale=$svc.Data.Sales | Where-Object Id -eq $discountSale.Id
Assert ($printedSale.Prints -eq 1 -and $printedSale.TicketPrints -eq 1) 'Impresión distingue comprobante y boletos'
# Render sin activar Loaded: no se abren diálogos de acceso ni se tocan datos del usuario.
$window=New-Object Miteatro.MainWindow
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$window.GetType().GetField('service',$flags).SetValue($window,$svc)
$window.GetType().GetMethod('Build',$flags).Invoke($window,@())|Out-Null
$window.Measure((New-Object Windows.Size(1320,880)));$window.Arrange((New-Object Windows.Rect(0,0,1320,880)));$window.UpdateLayout()
$bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap(1320,880,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$visual=[Windows.FrameworkElement]$window.Content; $visual.Measure((New-Object Windows.Size(1272,832))); $visual.Arrange((New-Object Windows.Rect(0,0,1272,832))); $visual.UpdateLayout(); $bitmap.Render($visual)
$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream=[IO.File]::Create((Join-Path $testRoot 'ui.png'));$encoder.Save($stream);$stream.Dispose()
Assert ($window.FindName('Tabs').Items.Count -eq 10) 'Administrador carga todos los módulos'
$tabs=$window.FindName('Tabs')
$tabs.SelectedIndex=3
$window.GetType().GetMethod('GenerateReport',$flags).Invoke($window,@())|Out-Null
$rows=$window.GetType().GetField('reportRows',$flags).GetValue($window)
Assert ($rows.Count -gt 1) 'Reporte diario genera resultados'
$net=[decimal]0
for($rowIndex=1;$rowIndex -lt $rows.Count;$rowIndex++){$net += [decimal]::Parse($rows[$rowIndex][6],[cultureinfo]::GetCultureInfo('es-MX'))}
$expectedNet=($svc.Data.Sales | Measure-Object Total -Sum).Sum-($svc.Data.Refunds | Measure-Object Amount -Sum).Sum
Assert ($net -eq $expectedNet) 'Reporte neto descuenta devoluciones'
foreach($index in @(1,3,4,6,8)){
 $tabs.SelectedIndex=$index
 $visual.Measure((New-Object Windows.Size(1272,832)));$visual.Arrange((New-Object Windows.Rect(0,0,1272,832)));$visual.UpdateLayout()
 $tabBitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap(1320,880,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $tabBitmap.Render($visual)
 $tabEncoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $tabEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($tabBitmap))
 $tabStream=[IO.File]::Create((Join-Path $testRoot ('tab-'+$index+'.png')));$tabEncoder.Save($tabStream);$tabStream.Dispose()
}
$window.Close()
Write-Output "TOTAL: $script:checks verificaciones correctas."

