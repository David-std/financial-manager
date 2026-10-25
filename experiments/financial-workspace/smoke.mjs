import { chromium } from "playwright-core";
import assert from "node:assert/strict";
import os from "node:os";
import path from "node:path";
const EDGE="C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const browser=await chromium.launch({executablePath:EDGE,headless:true,args:["--disable-gpu"]});
const page=await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
page.setDefaultTimeout(8000);
const errors=[];
page.on("pageerror",error=>errors.push(error.message));
const test=async(label)=>assert.equal(await page.getByText(label,{exact:false}).first().isVisible(),true,"Expected visible: "+label);
const nav=async(name)=>page.getByRole("button",{name,exact:true}).click();
const screenshot=async(name,fullPage=false)=>page.screenshot({path:path.join(os.tmpdir(),name),fullPage,animations:"disabled"});
const noOverflow=async(label)=>assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+3),false,label);
try{
  await page.goto("http://127.0.0.1:5178/",{waitUntil:"networkidle"});
  await test("Todo en orden"); await test("Entorno de ejemplo");
  await test("Registrar movimiento");
  await screenshot("FM-workspace-product-desktop.png");

  await nav("Mi trabajo");
  await test("Tu espacio de seguimiento");
  await test("Sin registros pendientes");
  await screenshot("FM-workspace-product-queue-empty.png");
  await nav("Movimientos");
  await test("Borradores de movimientos");
  await page.getByRole("button",{name:"Nuevo movimiento"}).click();
  await page.getByLabel("Importe (PEN)").fill("-10");
  await page.getByRole("button",{name:"Guardar borrador"}).click();
  await test("Introduce un importe positivo");
  await page.getByLabel("Importe (PEN)").fill("125,40");
  await page.getByLabel("Descripción").fill("Mantenimiento programado");
  await page.getByRole("button",{name:"Guardar borrador"}).click();
  await test("D-0001");
  await test("Cambios guardados en esta sesión");
  await screenshot("FM-workspace-product-expenses.png");

  await nav("Mi trabajo");
  await test("Mantenimiento programado");
  await test("D-0001");
  await test("campo(s) por revisar");
  await screenshot("FM-workspace-product-queue.png");
  await nav("Movimientos");
  await page.getByRole("button",{name:"Editar",exact:true}).click();
  await page.getByLabel("Descripción").fill("Mantenimiento ajustado");
  await page.getByRole("button",{name:"Guardar borrador"}).click();
  await test("D-0001");

  // Category hierarchy is powered by confirmed fixture data, not drafts.
  await nav("Categorías");
  await test("Distribución por categorías");
  assert.ok(await page.locator(".treemap-tile").count()>=5);
  await page.locator(".treemap-tile").first().click();
  await test("Movimientos de esta categoría");
  assert.ok(await page.locator(".explorer-record").count()>0);
  await page.getByRole("button",{name:"Lista",exact:true}).click();
  await page.getByRole("button",{name:"Ver movimientos"}).click();
  await test("Registro de movimientos");
  await page.getByRole("button",{name:"Restablecer"}).click();
  await page.getByRole("textbox",{name:"Buscar movimiento"}).fill("DEMO-EXP-0005");
  assert.equal(await page.locator("tbody tr").count(),1,"Real filter matches record");
  await page.locator("tbody tr").first().click();
  await test("DETALLE DEL MOVIMIENTO");
  await page.getByRole("complementary",{name:"Detalle del movimiento"}).getByRole("button",{name:"Cerrar detalle"}).click();

  // Budget plan Draft + 2 monthly category lines; no submit or approval.
  await nav("Presupuestos");
  await test("Prepara tu próximo ejercicio");
  await page.getByRole("button",{name:"Nuevo presupuesto"}).click();
  await page.getByRole("button",{name:"Guardar presupuesto"}).click();
  await test("Introduce un nombre");
  await page.getByRole("textbox",{name:"Nombre del plan"}).fill("Operación 2026");
  await page.getByRole("button",{name:"Añadir línea"}).click();
  await page.getByLabel("Importe line-1").fill("1500.50");
  await page.getByRole("button",{name:"Añadir línea"}).click();
  await page.getByLabel("Importe line-2").fill("300");
  await page.getByRole("button",{name:"Guardar presupuesto"}).click();
  await test("No repitas la misma categoría y mes");
  await page.getByLabel("Mes line-2").selectOption("2");
  await page.getByRole("button",{name:"Guardar presupuesto"}).click();
  await test("El presupuesto se ha guardado");
  await page.getByRole("button",{name:/Operación 2026/}).click();
  await test("Calendario de asignaciones");
  await test("S/ 1,800.50");
  await screenshot("FM-workspace-product-budget.png");

  await nav("Mi trabajo");
  await test("Operación 2026");
  await test("Mantenimiento ajustado");
  await screenshot("FM-workspace-product-queue-filled.png");

  await nav("Actividad");
  await test("Historial de actividad");
  await test("D-0001");
  await test("P-0002");
  await test("Sin identidad empresarial");
  await screenshot("FM-workspace-product-activity.png");

  await nav("Análisis");
  await test("Composición por mes");
  await page.getByRole("button",{name:"Cambiar a tema oscuro"}).click();
  assert.equal(await page.locator(".application.dark").count(),1);
  await page.getByRole("button",{name:"Cambiar a tema claro"}).click();
  assert.equal(await page.locator(".application.dark").count(),0);

  // On navigation records remain. Reload clears the local session.
  await page.reload({waitUntil:"networkidle"});
  await nav("Mi trabajo");
  await test("Sin registros pendientes");
  await nav("Actividad");
  await test("Los cambios de esta sesión aparecerán aquí");

  for(const [width,height] of [[320,700],[390,844],[430,900],[768,1024]]){
    await page.setViewportSize({width,height});
    await page.reload({waitUntil:"networkidle"});
    await noOverflow("Home root overflow at "+width);
    await screenshot(`FM-workspace-product-home-${width}.png`,true);
    if(width<=760)await page.getByRole("button",{name:"Abrir navegación"}).click();
    await nav("Categorías");await noOverflow("Category root overflow at "+width);
    await screenshot(`FM-workspace-product-category-${width}.png`,true);
    if(width<=760)await page.getByRole("button",{name:"Abrir navegación"}).click();
    await nav("Movimientos");await noOverflow("Transactions root overflow at "+width);
    if(width<=760)await page.getByRole("button",{name:"Abrir navegación"}).click();
    await nav("Presupuestos");await noOverflow("Budget root overflow at "+width);
    await screenshot(`FM-workspace-product-budget-${width}.png`,true);
    if(width<=760)await page.getByRole("button",{name:"Abrir navegación"}).click();
    await nav("Mi trabajo");await noOverflow("Work queue root overflow at "+width);
    await screenshot(`FM-workspace-product-queue-${width}.png`,true);
  }
  assert.deepEqual(errors,[],"No frontend exceptions");
  console.log("WORKSPACE_PRODUCT_E2E_PASS");
}finally{await browser.close();}
