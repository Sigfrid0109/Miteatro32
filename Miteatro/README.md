# Mi Teatro

Sistema de escritorio para Windows, entrada general y una sola caja, desarrollado en C# / WPF / .NET Framework 4.8.

## Inicio

1. Ejecuta `MiTeatro.exe` desde la carpeta de distribución completa. No separes el ejecutable de sus DLL y su archivo de configuración.
2. Crea el primer administrador con nombre, usuario y contraseña de al menos diez caracteres. No existen credenciales predeterminadas.
3. En **Catálogo**, registra funciones con fecha, sala, cupo y tarifas. Registra los productos y sus precios.
4. En **Inventario**, registra las existencias iniciales como una entrada con motivo.
5. En **Usuarios**, crea cuentas individuales de cajero y supervisor.
6. En **Ajustes**, configura el teatro, la impresora, los impuestos incluidos, el umbral de autorización y una carpeta de respaldos.
7. Inicia sesión como cajero, abre turno en **Caja** con el fondo inicial y comienza a vender.

La aplicación inicia con catálogos vacíos. Los datos de la demostración anterior permanecen en `demo.xml`; no se mezclan automáticamente con este sistema.

## Operación

### Venta

Selecciona función, tarifa y cantidad. Puedes combinar entradas y productos en un carrito. El sistema comprueba el cupo total del carrito y el inventario al cobrar. Las funciones canceladas o iniciadas dejan de venderse.

En efectivo, captura lo recibido y revisa el cambio. En tarjeta, realiza primero el cobro en la terminal externa y captura su referencia aprobada. En mixto, captura únicamente la parte pagada en efectivo; el resto se registra como tarjeta. No hay integración bancaria ni se almacenan datos de tarjeta.

Usa un cupón vigente si corresponde. Las promociones disponibles son porcentaje, monto fijo y 2x1. El 2x1 agrupa entradas de la misma función y tarifa, incluso agregadas en líneas separadas. Solo se aplica una promoción por venta. El descuento que supere el umbral configurado requiere un supervisor o administrador.

### Comprobantes y entrada

En **Ventas** puedes consultar el detalle, imprimir el comprobante y emitir un boleto individual por entrada. Cada boleto tiene un código único; las reimpresiones mantienen el mismo código. Los intentos de impresión quedan auditados. Si una impresora falla, consulta la venta guardada y vuelve a imprimir; no repitas el cobro.

El control de entrada permite capturar o escanear como texto el código completo del boleto. Rechaza códigos utilizados, anulados y funciones canceladas o de otra fecha. Los boletos de esta versión usan folio de texto, no QR.

### Devoluciones y cancelaciones

Selecciona una venta y usa devolución parcial o cancelación completa. Se exige motivo y autorización. Una devolución parcial indica artículo y cantidad. En productos puedes decidir si regresan a existencias; si no son revendibles, desmarca esa opción.

El reembolso utiliza los importes históricos de la venta y se reparte proporcionalmente entre efectivo y tarjeta, con ajuste al centavo. La parte de tarjeta se devuelve en la terminal externa y se registra su referencia. La parte de efectivo sale del turno actual. No es posible reembolsar más de lo vendido, devolver boletos utilizados o retirar más efectivo del esperado.

Cancelar una función impide nuevas ventas, pero no ejecuta reembolsos bancarios ni cancela automáticamente cada venta. Procesa las ventas afectadas desde **Ventas**.

### Caja

Solo hay un turno abierto. Otro cajero no puede operar ese turno. Supervisores y administradores pueden cerrar un turno ajeno cuando sea necesario.

El efectivo esperado es: fondo inicial + ventas en efectivo − reembolsos en efectivo + entradas − retiros. Las entradas y retiros requieren autorización y motivo. El corte conserva efectivo contado, esperado y diferencia. El historial permite consultar e imprimir cortes anteriores.

El carrito pendiente no se guarda al cerrar la aplicación. Las ventas cobradas y el turno abierto sí se conservan al reiniciar. El sistema pide confirmar antes de salir con un carrito pendiente.

### Reportes

Selecciona fechas y consulta resumen diario, ventas por función, producto o empleado, métodos de pago y ocupación. Los ingresos se registran en la fecha de venta y las devoluciones en la fecha del reembolso. Los cajeros consultan sus propias ventas; los responsables consultan todos los empleados. La ocupación muestra el cupo de las funciones del rango seleccionado. Los reportes se exportan a CSV con protección contra fórmulas introducidas en nombres.

## Permisos

| Operación | Cajero | Supervisor | Administrador |
| --- | --- | --- | --- |
| Venta y turno propio | Sí | Sí | Sí |
| Solicitar devolución o cancelación | Con autorización | Sí | Sí |
| Autorizar descuentos y movimientos | No | Sí | Sí |
| Inventario y ajustes | No | Sí | Sí |
| Reportes de todos y auditoría | No | Sí | Sí |
| Funciones, productos y promociones | No | Consulta | Sí |
| Usuarios, configuración y respaldos | No | No | Sí |

Las contraseñas se almacenan con PBKDF2-SHA256, sal individual y 210000 iteraciones. Hay bloqueo temporal después de cinco intentos fallidos. La sesión se bloquea por inactividad y al bloquear Windows. Los permisos se comprueban también en la capa de negocio.

## Almacenamiento y MySQL

Sin configuración adicional, los datos se guardan en `%LOCALAPPDATA%\MiTeatro\theater.xml`, mediante reemplazo atómico y copia anterior `.bak`. La carpeta pertenece a la cuenta de Windows; usa siempre la cuenta de Windows de la caja. `connection.xml` guarda la conexión, con la contraseña cifrada por Windows para esa cuenta.

Para usar MySQL:

1. Crea una base dedicada (puedes usar `database/create-database.sql`).
2. Crea en MySQL un usuario propio para la aplicación. Durante la primera conexión necesita CREATE, REFERENCES, SELECT, INSERT, UPDATE y DELETE sobre esa base.
3. Cierra el turno y abre **Ajustes → Configurar conexión MySQL**.
4. Captura servidor, puerto, base, usuario y contraseña. TLS se exige por defecto. La opción de desactivarlo se deja para un servidor local controlado.
5. La aplicación comprueba la conexión y crea sus tablas. Si el destino está vacío ofrece copiar los datos locales. Un destino con información no se sobrescribe automáticamente. Inicia sesión con las credenciales del destino.

Las tablas `mt_users`, `mt_performances`, `mt_products`, `mt_shifts`, `mt_sales`, `mt_lines`, `mt_refunds`, `mt_tickets`, `mt_promotions`, `mt_cash_movements`, `mt_stock_movements` y `mt_audits` tienen claves primarias y relaciones. Cada entidad conserva además su representación XML versionada. `miteatro_state` contiene configuración y revisión. Las escrituras modifican solo las filas nuevas o cambiadas y se confirman en una transacción InnoDB con control de revisión. Los reportes se generan en la aplicación; el XML por entidad no está pensado para consultas analíticas SQL directas.

Si hay un fallo de conexión durante el cobro, reinicia y consulta el historial antes de repetirlo. Si MySQL no está disponible, el sistema no cambia silenciosamente a datos locales.

## Respaldos y recuperación

Configura una carpeta externa o sincronizada. Mientras la aplicación está abierta mantiene un respaldo por día, actualizado después de las operaciones y de forma periódica. Los errores se muestran en la barra de estado.

**Programar respaldo diario** registra `MiTeatroDailyBackup` en el Programador de tareas de Windows a las 02:00. Ejecuta `MiTeatro.exe --backup`, incluso con la aplicación cerrada, siempre que el equipo esté encendido y la sesión de Windows esté iniciada. Conserva la ubicación del ejecutable; si la cambias, vuelve a programarlo. Puedes consultar el resultado de ejecución y cambiar el horario en el Programador de tareas. No se instala ninguna tarea hasta pulsar ese botón.

Para restaurar, cierra el turno actual y usa **Ajustes → Restaurar respaldo**. Se crea una copia previa y se comprueba la integridad antes de sustituir los datos. Debes conocer las credenciales guardadas en el respaldo. Un turno abierto en el respaldo se recupera tal como estaba. Los respaldos contienen información del negocio y hashes de usuarios: guarda la carpeta con acceso restringido.

Los logs se guardan en `%LOCALAPPDATA%\MiTeatro\errors.log`. No se ofrece recuperación de contraseña sin administrador: conserva un respaldo y al menos una cuenta administradora accesible.

## Compilación y pruebas

Abre `Miteatro.csproj` en Visual Studio con herramientas de escritorio .NET y .NET Framework 4.8. Restaura NuGet y compila. El conector MySQL está fijado en la versión 9.6.0.

```powershell
MSBuild.exe Miteatro.csproj /restore /p:Configuration=Release
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\test-system.ps1
```

El script de pruebas usa datos aislados en `test-output`, sin abrir los datos del usuario. Incluye permisos, pagos mixtos, cupo, inventario, idempotencia, devoluciones, descuentos, cortes, persistencia, conflictos, respaldos y reportes. Renderiza las pantallas para revisión visual. Para probar MySQL, `-MySql` requiere una instancia de prueba desechable en `127.0.0.1:33317`, usuario root sin contraseña; nunca apunta al servicio MySQL habitual de la computadora.

Se verificaron 37 casos con almacenamiento local y 37 con MySQL 9.4 aislado usando TLS. La impresora física, la terminal bancaria y la programación de tareas en el equipo definitivo requieren una prueba operativa. La aplicación no emite facturas fiscales ni realiza cobros bancarios automáticos.


