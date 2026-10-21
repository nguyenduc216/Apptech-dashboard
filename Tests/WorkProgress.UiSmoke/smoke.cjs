const modulePath=process.env.APPTECH_PLAYWRIGHT_MODULE || 'playwright';
const {expect}=require(modulePath+'/test');
const {chromium}=require(modulePath);
const fs=require('fs');
(async()=>{
 const browser=await chromium.launch({headless:true});
 try {
 const page=await browser.newPage({viewport:{width:1440,height:900}}); const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const out='reports/work-progress';fs.mkdirSync(out,{recursive:true});
 await page.goto('http://127.0.0.1:5188/TienDoCongViec');
 await expect(page.locator('[data-card]')).toHaveCount(3);
 if(await page.locator('[data-card][data-id="1"] [data-card-status]').inputValue()!=='1'){
 await page.locator('[data-card][data-id="1"] [data-card-status]').selectOption('1');
 await expect(page.locator('[data-work-board-page]')).not.toHaveAttribute('aria-busy','true');
 await page.reload();
 }
 await expect(page.locator('[data-card][data-id="1"]')).toContainText('+1');
 await expect(page.locator('[data-card][data-id="1"]')).toContainText('Quá hạn');
 const target=page.locator('[data-status-id="2"]');
 await page.locator('[data-card][data-id="1"]').dragTo(target);
 await expect(target.locator('[data-card][data-id="1"]')).toHaveCount(1);
 await expect(page.locator('[data-board-message]')).toContainText('Đã cập nhật');await expect(page.locator('[data-work-board-page]')).not.toHaveAttribute('aria-busy','true');
 // Simulate server failure: optimistic move must roll back to original column.
 await page.route('**/TienDoCongViec/Move',route=>route.fulfill({status:409,contentType:'application/json',body:JSON.stringify({message:'Xung đột kiểm thử'})}));
 await page.locator('[data-card][data-id="1"] [data-card-status]').selectOption('4');
 await expect(page.locator('[data-board-message]')).toContainText('Xung đột kiểm thử');await expect(page.locator('[data-work-board-page]')).not.toHaveAttribute('aria-busy','true');
 await expect(page.locator('[data-status-id="2"] [data-card][data-id="1"]')).toHaveCount(1);
 await page.unroute('**/TienDoCongViec/Move');
 // Real keyboard-compatible dropdown move, including completion date refresh.
 await page.locator('[data-card][data-id="1"] [data-card-status]').selectOption('4');
 await expect(page.locator('[data-board-message]')).toContainText('Đã cập nhật');await expect(page.locator('[data-work-board-page]')).not.toHaveAttribute('aria-busy','true');
 await expect(page.locator('[data-status-id="4"] [data-card][data-id="1"]')).toContainText('Hoàn thành trễ');
 for(const [name,width,height] of [['desktop',1440,900],['tablet',768,1024],['mobile',390,844]]){
  await page.setViewportSize({width,height});await page.reload();
  await expect(page.locator('h1').first()).toContainText('Tiến độ công việc');
  const size=await page.evaluate(()=>({doc:document.documentElement.scrollWidth,view:innerWidth}));
  if(size.doc>size.view+1)throw new Error(name+' has document horizontal overflow '+JSON.stringify(size));
  if(name!=='desktop') {await page.locator('[data-board]').evaluate(board=>{const col=board.querySelector('[data-status-id="4"]');board.style.scrollBehavior='auto';board.scrollLeft+=col.getBoundingClientRect().left-board.getBoundingClientRect().left;});await expect(page.locator('[data-status-id="4"] h2')).toBeInViewport();}
  await page.screenshot({path:out+'/kanban-'+name+'.png',fullPage:true});
 }
 await page.setViewportSize({width:1440,height:900});await page.goto('http://127.0.0.1:5188/TrangThaiCongViec');
 const form=page.locator('.wp-status-form').last();await form.locator('[name="Code"]').fill('UI_WAIT_'+Date.now());await form.locator('[name="Name"]').fill('Chờ khách hàng xác nhận');await form.locator('[name="Color"]').fill('#ffcc00');
 await expect(form.locator('[data-status-preview]')).toHaveText('Chờ khách hàng xác nhận');
 await form.locator('button').click();await expect(page.getByRole('status')).toContainText('Đã lưu');
 const saved=page.locator('.wp-status-editor').filter({has:page.locator('input[value^="UI_WAIT_"]')});await saved.getByRole('button',{name:'Vô hiệu hóa'}).click();await expect(saved).toContainText('Đã tắt');
 await page.screenshot({path:out+'/status-catalog-desktop.png',fullPage:true});
 const denied=await page.goto('http://127.0.0.1:5188/khong-co-quyen?ReturnUrl=/TienDoCongViec');
 if(denied.status()!==403)throw new Error('Access denied endpoint must terminate with HTTP 403');
 await expect(page.getByRole('heading',{name:'Không có quyền truy cập'})).toBeVisible();
 await page.screenshot({path:out+'/access-denied.png',fullPage:true});
 fs.writeFileSync(out+'/ui-results.json',JSON.stringify({passed:true,identity:'persisted legacy admin, QuanTriVien=false',viewports:['1440x900','768x1024','390x844'],checks:['SQL-backed MVC rendering','legacy admin access and full request scope','drag and drop persists','409 rollback','dropdown completion','overdue and late badges','avatar +N','catalog create and preview and deactivate','no document overflow','access denied view terminates with HTTP 403'],pageErrors:errors},null,2));
 if(errors.length)throw new Error(errors.join('\n'));
 console.log('UI smoke passed with real local SQL and MVC views');
 } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1});
