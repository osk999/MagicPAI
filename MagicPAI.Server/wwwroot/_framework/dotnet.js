//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"a612c2a1056fe3265387ae3ff7c94eba1505caf9",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "MagicPAI.Studio",
  "resources": {
    "hash": "sha256-hAuJV3IbTP22ZwpV47z32yIRfxCymm88+VqSWw3+qPw=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.ykrnppwhq2.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.peu2mfb29t.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.53ez3dx5uy.wasm",
        "integrity": "sha256-Ebk+Km0uqtdo/srKe0YcuUOlFykCcKVkBt03gTWt0aU=",
        "cache": "force-cache"
      }
    ],
    "icu": [
      {
        "virtualPath": "icudt_CJK.dat",
        "name": "icudt_CJK.tjcz0u77k5.dat",
        "integrity": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_EFIGS.dat",
        "name": "icudt_EFIGS.tptq2av103.dat",
        "integrity": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_no_CJK.dat",
        "name": "icudt_no_CJK.lfu7j35m59.dat",
        "integrity": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
        "cache": "force-cache"
      }
    ],
    "coreAssembly": [
      {
        "virtualPath": "System.Private.CoreLib.wasm",
        "name": "System.Private.CoreLib.5d8pdo2ln8.wasm",
        "integrity": "sha256-meOedqCPZcVKh1dJDi8nspFxlK17bkBF2Eq+pxYRwTs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
        "name": "System.Runtime.InteropServices.JavaScript.v2on4s360k.wasm",
        "integrity": "sha256-WQK8O/IItGzA/3XWAUdBLVbG3o27q/3HA8A/eJzO9b8=",
        "cache": "force-cache"
      }
    ],
    "assembly": [
      {
        "virtualPath": "BlazorMonaco.wasm",
        "name": "BlazorMonaco.i33mlyw1st.wasm",
        "integrity": "sha256-LvYzL5DdU2cB0aD0vdq7fb6O1CcuZvp3OxCq/HCckhk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Blazored.FluentValidation.wasm",
        "name": "Blazored.FluentValidation.x0xsecpmtx.wasm",
        "integrity": "sha256-MSQuUWmw/uBMui2/n4kdEKi0h5j0G+EuO1sM6KK6ltM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Blazored.LocalStorage.wasm",
        "name": "Blazored.LocalStorage.12n6dz54qr.wasm",
        "integrity": "sha256-OaMAAd5n7ORfyur5e3QIyEVKJ76MKIvwbg7/icnnYcU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "CodeBeam.MudBlazor.Extensions.wasm",
        "name": "CodeBeam.MudBlazor.Extensions.bapi9rl0fj.wasm",
        "integrity": "sha256-g5WKhFTKfYA+2Bj+fBcE4dsgOitej9P5k4kimlfvOtU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "CsvHelper.wasm",
        "name": "CsvHelper.5c97h8cn3b.wasm",
        "integrity": "sha256-BXz5RCSFBrg3KCDZD4lxgyWmw1YH6vUT0N/oIQdCKfA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DEDrake.ShortGuid.wasm",
        "name": "DEDrake.ShortGuid.xvdfruyxwj.wasm",
        "integrity": "sha256-DYdQGjRgO7IhiqcKqcnznHqwzOObOJgoIY3wI+Tn1X4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Docker.DotNet.wasm",
        "name": "Docker.DotNet.j86n1kbf43.wasm",
        "integrity": "sha256-rZ81X7EXJJqK5JB14+XO7Kr82bTgEMFzbq0i/9QwG1A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Api.Client.wasm",
        "name": "Elsa.Api.Client.uiq96cygav.wasm",
        "integrity": "sha256-hoS6Eb1VMfVtrGA7NYZjxvxj0LPIsEME6lKHiE9hKJo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.wasm",
        "name": "Elsa.Studio.6gdjm6hqzz.wasm",
        "integrity": "sha256-KvccQWO5pd64H4XiD11KfemY0c7fZajp1umuMGfufpU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.ActivityPortProviders.wasm",
        "name": "Elsa.Studio.ActivityPortProviders.rtxusu47b2.wasm",
        "integrity": "sha256-QSUaBOaVis/tY+Z1JqiP1Jv9ZItSKVVTbjJSFXVq91g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Core.BlazorWasm.wasm",
        "name": "Elsa.Studio.Core.BlazorWasm.pemlrn20gn.wasm",
        "integrity": "sha256-sTvy1/OC6DUnQRhIFnG3tCG6cpIkmRoFKJL266UDTcI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Core.wasm",
        "name": "Elsa.Studio.Core.c6zv6mtoht.wasm",
        "integrity": "sha256-1HEavaf1Uhx1RU7BsTQW126wSbG0X33iHNCx28ibjaQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Dashboard.wasm",
        "name": "Elsa.Studio.Dashboard.d4bop53nms.wasm",
        "integrity": "sha256-TSsy4nRHKuYWWylYVNtJR5d/smy1vkq3zkn7yI3xmmY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.DomInterop.wasm",
        "name": "Elsa.Studio.DomInterop.ea6dcw9c9e.wasm",
        "integrity": "sha256-6gJ7xjcpyPiYns70ljsHIPapH0vCD97PKC9dPFkbWhM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Login.BlazorWasm.wasm",
        "name": "Elsa.Studio.Login.BlazorWasm.an1tbq995e.wasm",
        "integrity": "sha256-lhBY0+fyT54L1gwxdNMbzVi18g4Q89kanWD2tAY46hs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Login.wasm",
        "name": "Elsa.Studio.Login.kicl41ocqp.wasm",
        "integrity": "sha256-nNoynexrtgRKpNIuQ4ibNB86wTG7ypCduqilGe3AFvI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Shared.wasm",
        "name": "Elsa.Studio.Shared.3abqo4y32q.wasm",
        "integrity": "sha256-m0PZ37gM9I58nycXHtwEvvxH+UwfgtJu+oa158QSTeY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Shell.wasm",
        "name": "Elsa.Studio.Shell.imy54pxkr8.wasm",
        "integrity": "sha256-diniEkkMYq7hGD0K8Kkkdmohq6QJb8AovNPWlbpcvKI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.UIHints.wasm",
        "name": "Elsa.Studio.UIHints.cwi0qi4csn.wasm",
        "integrity": "sha256-rIHpBMcqVaYyDNNedhCIe0rSIvEAndyDLmv6Jor6YKs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Workflows.Core.wasm",
        "name": "Elsa.Studio.Workflows.Core.i8o48z5onc.wasm",
        "integrity": "sha256-shfd6+bU0FdeqSfnIHFwDQU+dD6UhR/Sfs1sXTQ2Abw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Workflows.Designer.wasm",
        "name": "Elsa.Studio.Workflows.Designer.rfwepbijq1.wasm",
        "integrity": "sha256-t0B8HyFqdcVyFGQhW2Pq3cNBcL+BqdPfK75+aaWUyBA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Elsa.Studio.Workflows.wasm",
        "name": "Elsa.Studio.Workflows.vbw1xhmgvt.wasm",
        "integrity": "sha256-FaHphM0WrxWekVTuIAODH5mTFwU4WJ6fbOza1bZSLVE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "FluentValidation.wasm",
        "name": "FluentValidation.b28mz0qrx7.wasm",
        "integrity": "sha256-UzXm5ONFC2YfuSahJleOJiIJNURatHcRQIkXq2hoPew=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Humanizer.wasm",
        "name": "Humanizer.oqup3v7t3k.wasm",
        "integrity": "sha256-4NbSboZzzP9nikRtXapUZNzOyITt7ht9TNqCIQHr5OE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MagicPAI.Core.wasm",
        "name": "MagicPAI.Core.dh4qctpuf3.wasm",
        "integrity": "sha256-McmJINOw23YWenW6qPrYM4izf2wSDRqNcu0jb/400h4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MagicPAI.Studio.wasm",
        "name": "MagicPAI.Studio.wzxvud3rb3.wasm",
        "integrity": "sha256-3N+WVFuLFS8aEfG06iRPkMGaa883bPpIZU77eQMrc+Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Authorization.wasm",
        "name": "Microsoft.AspNetCore.Authorization.jmlyapa3lu.wasm",
        "integrity": "sha256-+08xo5c+tD42w/R54z5gqLSqwEUe7b42U9hOajsG7LM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Authorization.wasm",
        "name": "Microsoft.AspNetCore.Components.Authorization.omnb5g9atj.wasm",
        "integrity": "sha256-YMFhWusQR2gBMYxU+t2vLYUBrdrF2vyV2b0hc5rnX0A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Forms.wasm",
        "name": "Microsoft.AspNetCore.Components.Forms.mhw5nyj0zg.wasm",
        "integrity": "sha256-uqIMWGdulbVC5irIvZ33j+oApuYChu/6THUHKOBFA7c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Web.wasm",
        "name": "Microsoft.AspNetCore.Components.Web.vxzk2eh0dl.wasm",
        "integrity": "sha256-DcM3vxEiwvzPbewK5ijJsZHZjFdzndzMGnUCMcoegBI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.WebAssembly.wasm",
        "name": "Microsoft.AspNetCore.Components.WebAssembly.b7q5kqkyt2.wasm",
        "integrity": "sha256-5w/A76qQt1IPbGTbOLlk0QMz9uTwHnUCkjkHyfKSLTk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.wasm",
        "name": "Microsoft.AspNetCore.Components.tmgcmpesh4.wasm",
        "integrity": "sha256-y2Zm+1P6yJzmjvMe1vHhRAGZqXVg5QQuAmHtm2EFAgo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Connections.Abstractions.wasm",
        "name": "Microsoft.AspNetCore.Connections.Abstractions.vrmh7xlm03.wasm",
        "integrity": "sha256-dReva1n3fJeViQ1xct7pAFJ+hZ/uTRPGxgkZyVOVeX8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Http.Connections.Client.wasm",
        "name": "Microsoft.AspNetCore.Http.Connections.Client.zctbxyi965.wasm",
        "integrity": "sha256-Y1KZORRm5KMN/npfFJH3nDosTmrITKukH9caWhBe10s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Http.Connections.Common.wasm",
        "name": "Microsoft.AspNetCore.Http.Connections.Common.u4x48a313q.wasm",
        "integrity": "sha256-vmBBNmW8PwwvZddo1DFxJVaRoH5+r3nCH4VgpbCV5P0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Metadata.wasm",
        "name": "Microsoft.AspNetCore.Metadata.egkir8gywr.wasm",
        "integrity": "sha256-DtMn+E4bymcQuGBg0igp+sZ5tmReWI2OCNtQ4RmKLy0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.SignalR.Client.Core.wasm",
        "name": "Microsoft.AspNetCore.SignalR.Client.Core.y96ozbj0kr.wasm",
        "integrity": "sha256-xnSleCg2rfpY1t+xmVKKo0JJhCLH+H8fF/vqwuhJ1ps=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.SignalR.Client.wasm",
        "name": "Microsoft.AspNetCore.SignalR.Client.ua9jl2837f.wasm",
        "integrity": "sha256-Ko0sJ6HDXcKCWrDUUGJvxwvivGyphsiDXpzBM5HUZ8k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.SignalR.Common.wasm",
        "name": "Microsoft.AspNetCore.SignalR.Common.ybkc3hestm.wasm",
        "integrity": "sha256-EmcuU1Q0AtvXNOWuBFgTtbzTq1z1OrbarfqAU4kft2s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.SignalR.Protocols.Json.wasm",
        "name": "Microsoft.AspNetCore.SignalR.Protocols.Json.vg7cc3rp3b.wasm",
        "integrity": "sha256-MedpLyAxM9mzV2Z2ZJ3HfMjFytXm0cMGyBoUgiIDB+Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.WebUtilities.wasm",
        "name": "Microsoft.AspNetCore.WebUtilities.0bbkugez9q.wasm",
        "integrity": "sha256-xMtPePfi23+QUJchfwxCXAf7wBZ15crHk7KWPRXOYGM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CSharp.wasm",
        "name": "Microsoft.CSharp.55y6kkd2xa.wasm",
        "integrity": "sha256-WaaTqo8HUMoSQJz3AOlT3yTDS/+zwNTQTbJzgeRZsz4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.DotNet.HotReload.WebAssembly.Browser.wasm",
        "name": "Microsoft.DotNet.HotReload.WebAssembly.Browser.hrhwg3a64c.wasm",
        "integrity": "sha256-7T3xXhofp38yBmdv6CI7STwXMpOeEHm6J4GkYs//NlU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.wasm",
        "name": "Microsoft.Extensions.Configuration.bnnnwrlqxf.wasm",
        "integrity": "sha256-iey/f7S067jp5dpeRM1r/qq+TdDwbHJ72Fx/klXcG48=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
        "name": "Microsoft.Extensions.Configuration.Abstractions.5vwfzdf4ut.wasm",
        "integrity": "sha256-IZtDQfbVWilPCyzICOIJDJ0fFVRcwCh8Y9FyuZ+KLmE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
        "name": "Microsoft.Extensions.Configuration.Json.8b348l7eix.wasm",
        "integrity": "sha256-PKr5X82A4mD/89Jvh+zjdrqh2eBlJpu7XWEQa3ydKJU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.ln7s0l71uz.wasm",
        "integrity": "sha256-wTnVFCpOWjV/N0AJT+823Ri5dqVqo96JKNFypgHLy5A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.mjxtanh6qd.wasm",
        "integrity": "sha256-EuXNGsp2Oxo8Vo45zd69D0yuTAb/IlT9tw0IRqQ5+Rw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.Abstractions.wasm",
        "name": "Microsoft.Extensions.Diagnostics.Abstractions.ayu2tao62m.wasm",
        "integrity": "sha256-Mq4cgIa9dPNJp990Fa+2OvaVHthzDx0TdSYgMoRj9jY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.wasm",
        "name": "Microsoft.Extensions.Diagnostics.nhsqrevfo5.wasm",
        "integrity": "sha256-u7XA8KJvfEwHWazYrC53NgjHgbF1iAYb1t01mqpVOB8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Features.wasm",
        "name": "Microsoft.Extensions.Features.hgc6v2fpgg.wasm",
        "integrity": "sha256-b4SVr8PXl8PR+9IfEDJDf9fl2ozFTBpkjNY3jCyi3Ig=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Http.wasm",
        "name": "Microsoft.Extensions.Http.fpaqpubbbu.wasm",
        "integrity": "sha256-sxlohTi9MaoEVJeUc9+66l0EGAE3EQnXYR1QRu3tBJg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Http.Polly.wasm",
        "name": "Microsoft.Extensions.Http.Polly.i3nwyh61g3.wasm",
        "integrity": "sha256-7iK41dHa+CA06N40fh2N8+8N+8/AEZXuSr2dHzGXQ9w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Localization.Abstractions.wasm",
        "name": "Microsoft.Extensions.Localization.Abstractions.xofp6sxj6c.wasm",
        "integrity": "sha256-T16Ei012/uCQt3d6MLcU6gU8/YWemRTp7bYRlNBcZd4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Localization.wasm",
        "name": "Microsoft.Extensions.Localization.bvn14pws96.wasm",
        "integrity": "sha256-6UgMJoVZBfDdfzYR0aKVK6BWArxpXC1qiQDDjiXw/L4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
        "name": "Microsoft.Extensions.Logging.Abstractions.d5xigbc5hq.wasm",
        "integrity": "sha256-UZRhRrkEKKM+XJwSCJxlHHL9TFDxiuD9zvgJkX0e/NE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.wasm",
        "name": "Microsoft.Extensions.Logging.0es5obd26b.wasm",
        "integrity": "sha256-+JJyZu26hY1DeJH0Y0mbDAjdEtj6zHZ/gvlLlNAUidk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.wasm",
        "name": "Microsoft.Extensions.Options.8r61pfos18.wasm",
        "integrity": "sha256-jFJFOHd0++OO4p+maKu+imuFc113s7s3ZCbRYtmoV/0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Primitives.wasm",
        "name": "Microsoft.Extensions.Primitives.cp31hw3q65.wasm",
        "integrity": "sha256-AFZda5bm4Rnet5nE5kWHVJdiXA4r8lqIRv8hIJ1rYuU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Validation.wasm",
        "name": "Microsoft.Extensions.Validation.vghklfoue1.wasm",
        "integrity": "sha256-b2af5aKOsIwiFN45y90oQU5zFkAEwqljM+LdYEsArnk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.WebAssembly.wasm",
        "name": "Microsoft.JSInterop.WebAssembly.zfll97oa4n.wasm",
        "integrity": "sha256-MXuXZOuv2Tjh4/SBs/yYzqzG93eSGGiQTGSbWVWbLxc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.wasm",
        "name": "Microsoft.JSInterop.wzha9684fu.wasm",
        "integrity": "sha256-ZIVZU0J6uuPKgYw+FTDrnroC2q1W2IQwHmDayiuykiM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MudBlazor.wasm",
        "name": "MudBlazor.tumq4p8vph.wasm",
        "integrity": "sha256-y0FN9oXIPgGPxba3E/br+27wyweayvMytsofAnyVfG0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Newtonsoft.Json.wasm",
        "name": "Newtonsoft.Json.ryat7hvx56.wasm",
        "integrity": "sha256-PaicHOw1U7HF05kEvZI0Rgg1nwdA8hrzbzdkw1qgUuU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Polly.Core.wasm",
        "name": "Polly.Core.jm6rexv946.wasm",
        "integrity": "sha256-S3y4Hd8VmJcgynVN2RrypaHtm2ThDyUvnrSuKdQ7H5Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Polly.Extensions.Http.wasm",
        "name": "Polly.Extensions.Http.aamf4ift6v.wasm",
        "integrity": "sha256-bqX9l2dLJ0m0IilNsXvbO14i9Lx0oDeudAGIsS3kEAw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Polly.wasm",
        "name": "Polly.qpk1anju22.wasm",
        "integrity": "sha256-4iomuwTmtC/1w+peVKYV47bilFC10eGON79ytQp7KXU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Radzen.Blazor.wasm",
        "name": "Radzen.Blazor.qndytsqcki.wasm",
        "integrity": "sha256-Wrn3nsN1lUe/P69cOk13P6QDqifRnr4nH4ZOZdFysJc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Refit.HttpClientFactory.wasm",
        "name": "Refit.HttpClientFactory.quzw5yd9f1.wasm",
        "integrity": "sha256-gnrYn04A5T89mEVJdmiSmD8tsRnKREtoQFsldYStTZI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Refit.wasm",
        "name": "Refit.sogb4xk4un.wasm",
        "integrity": "sha256-PBFuCYJUT1pPAlB9SNhwH7I4UpQHGkhYYxC7taY8KBQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.ib0580ev6d.wasm",
        "integrity": "sha256-IsfY+caLUXS2TZ5Ww1SdK7K2j5lliAqA9Op+uRTQpDc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.648y1wh82f.wasm",
        "integrity": "sha256-anFpZm3wFJHD7MAvSX9raXVRo62xt/pLgltnSqkB0aU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.0ygij72yc9.wasm",
        "integrity": "sha256-uMpOjmwLR3WivB8UNyRsQoWS3ujcCEJsZ/D5s0HiE0k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.x2p82bbw09.wasm",
        "integrity": "sha256-B9HPPla9f6bQZ4Ssogl0qIm38Nv8ifkMAqc50BBl96Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.bwtmofmh1u.wasm",
        "integrity": "sha256-JWsSzk1424WckVXHAXT8hzusWVcRbicgJUwd7S+WVmA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.i7j2qw5b5w.wasm",
        "integrity": "sha256-QkhyNe66VLm8zVA/ORx6aZafkxkDNNChrcNPFboyWj0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.46f63oyhcl.wasm",
        "integrity": "sha256-tMpM7noEiNS6/sB1I8tw+PjhC+GPIInuCnczSDOEdbw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.hiyk2hrmhm.wasm",
        "integrity": "sha256-uUEwRn3cGq0pZDZ9epmK7hmtR0YH6IVS+3hrKMc0SLg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.2jkz233oo1.wasm",
        "integrity": "sha256-Uk9EihNhP539qENPuyaDyOQLcmPKtre6mmoFoHusjoo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.aptpistts8.wasm",
        "integrity": "sha256-YktoXfg6Q+zdHBTnWRLS3l/l7M5RM3rCXI037Hot2xo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.0tv4u68zrv.wasm",
        "integrity": "sha256-/I9ZnFhG9aScmC/4Hi8pqSKEh7wOUicrUIeUVtkemZg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Core.wasm",
        "name": "System.Core.y6ehs2hb3d.wasm",
        "integrity": "sha256-5ulsXGEL4qfPH1yCs9sK8ZFHPBx1QsUMLMMzTv1pxRY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.m8flhojh4x.wasm",
        "integrity": "sha256-mj7l54jkQynZ3kKMLymf6FPimTKL64icYS40nJ0lVgY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.479u8sixsq.wasm",
        "integrity": "sha256-8B0a7Z0h+k/X/MAa45DhrkUXKenbgD6OH3wI5Jk5CEM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.ebr18usx1l.wasm",
        "integrity": "sha256-ioA+zMQRp04OAu+C18QBydoZn6CCMO9epWElfmWItQo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.h6qcdpc88w.wasm",
        "integrity": "sha256-d/+wc73z56aJsQlsQ2E+DnD7DdmvJiT+kfkkv4gLHUU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.Primitives.wasm",
        "name": "System.Drawing.Primitives.mh3t08p6wx.wasm",
        "integrity": "sha256-6uYb6J+p4zATKw1AJh6Ms/WUrYa5JjtZbdFn/GxZenc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.wasm",
        "name": "System.Drawing.lm99lnbtcj.wasm",
        "integrity": "sha256-A4avC+hwtLfPMaFvzZqfsdvZRJDEONsLALt/UQ+Udj4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.yfhd9bryn6.wasm",
        "integrity": "sha256-jn4/j/MkLh9HX3veLrFWICwl0sjcRsChKDpWMmMsw3A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.ups4cm91h0.wasm",
        "integrity": "sha256-niH4hY72WC7y3s9BaL5FTWjij6XrEc0D2/+AUYYtUOk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.wasm",
        "name": "System.IO.Pipes.m6wms0r2ww.wasm",
        "integrity": "sha256-kud+DWGO+nSyf3atdYpEN6NtStbt1J8YCY8eN7iD9s4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.i2z2rmd50g.wasm",
        "integrity": "sha256-9r7ECenFuPraPDpJylIO+myAFVFRzo+iJbr0NH8lzEM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Queryable.wasm",
        "name": "System.Linq.Queryable.4y3rc4xth4.wasm",
        "integrity": "sha256-REdPErZdDDWiig6FmtDD+lrT/Ore70pYhEbhEBx09xo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.uk5zo8w7lx.wasm",
        "integrity": "sha256-AgG8V88l6ZfNgSntDH4B6B06cMcwYFiM38AqKQSp5og=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.117sa7xupc.wasm",
        "integrity": "sha256-lWh029y6rFJSUdwrqX9lOnb/fRRceZ8gOrAxIf3kwko=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.nyvizbh69x.wasm",
        "integrity": "sha256-TxFmLTE/G4NN70xgOSYrhEr3ChksrZVnWngySBhWVmc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.Json.wasm",
        "name": "System.Net.Http.Json.9mrw7h5f6f.wasm",
        "integrity": "sha256-L1H9GylpfAvsjFyHjroPx/1Yd7fpzl3Eusso9nUfKsQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NameResolution.wasm",
        "name": "System.Net.NameResolution.22ak4stdyu.wasm",
        "integrity": "sha256-6vNmqkxUPttFEQYuaUf0+sjhmVmefjOde+ugc0S/xfs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.sgrlm0blij.wasm",
        "integrity": "sha256-TETCjCzKZ/swhCE0/OJkMhnsh1Rf67UwRHK5xlV+H5g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.vaz8zkcdb8.wasm",
        "integrity": "sha256-tzclJMyAIGdmp5IBzqJnSTasLuTxz92UnRTw2edGoqs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Security.wasm",
        "name": "System.Net.Security.qe4doc1crf.wasm",
        "integrity": "sha256-ki9Rw/pEHDkzpj/SK9X90AkmiCvt4fOQL9CNiLlFhMc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServerSentEvents.wasm",
        "name": "System.Net.ServerSentEvents.nfwcdj5wl9.wasm",
        "integrity": "sha256-zSdTVJx0lEP9Dn24avIAW9aJNeyaN8xkQHpdnxreN5k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.2y4ae23wdh.wasm",
        "integrity": "sha256-in0p0NeIrtN64wX/G9WMwSoxJqO34ar+vo5L2NUR5rI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.Client.wasm",
        "name": "System.Net.WebSockets.Client.qolyrlh6yg.wasm",
        "integrity": "sha256-SqGjjY3MmwgmfpllZAD0C+eYwtdO/3HaSBV55wXJ5mg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.16owgmhecf.wasm",
        "integrity": "sha256-pwUVq4vyUub+dGjxQ2eZ6KozN/yPp6QOvQ2kkj07r5U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.2vdjxu6b12.wasm",
        "integrity": "sha256-7uVK2G+Te7gDpN9+DSh8lhrgDLKq+YmPgtu0hN7aLDs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.jzd6ggry87.wasm",
        "integrity": "sha256-TwzYysL8EiANwIoibNP3LLHKdjrZf9iiht4Ms761WqI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.xtrnj7ks0t.wasm",
        "integrity": "sha256-pZuyEFeOj/pM+/qaaMbTfm5w7XwHSWJ2mr5xzfh7L4c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.hd1mf6h14n.wasm",
        "integrity": "sha256-dUcuUC2jqEpLgMsmhZa9sEuqomzlG5QFA43jy0R8lXU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.DispatchProxy.wasm",
        "name": "System.Reflection.DispatchProxy.sz6kbmmw6q.wasm",
        "integrity": "sha256-aMOjU9VbSntcy7L81xiKRoPMw2RNlcTfC/jE9Ujjyfw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.wasm",
        "name": "System.Reflection.Emit.26yhp0m22z.wasm",
        "integrity": "sha256-OmBzDK/mRbIgyLv6dd+1Fp9wMXiDiLMc0pyY5zfg9LE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.k3gx4rve53.wasm",
        "integrity": "sha256-QPBWGVROc8KORxXy92bQSyAoq6BtUIb5OuwGzfIs8m8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.m3toqtoxi1.wasm",
        "integrity": "sha256-rqgJ0FJMiGbac0th+CZPhijbkb/tAWeMPzIyqeWl4Ig=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.fb38m0fm4i.wasm",
        "integrity": "sha256-NfeeERGKalqTg7j+Ap+3MYfO+htQt7d16ixYoRAexRM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.da50kn87d3.wasm",
        "integrity": "sha256-O1WVLxSPz9/hiemI+9nF61b3TpMpCbFtzze/m7k2II0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.u17ldq6l3o.wasm",
        "integrity": "sha256-8osJQXqFoTQBQBFxVf4tddCl3Wg1ribmHt4cbGptlEU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.po0kxfrt8e.wasm",
        "integrity": "sha256-h3LAEtw+9zvwJraLhgNFnPwT8UuiM1uelxlyC+589Gc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.n8b7d6hao7.wasm",
        "integrity": "sha256-WTarGaspGsOySX+yL2K1eCQy5GCWypP+SNpSU1tbfMA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.wkjt2lfzyp.wasm",
        "integrity": "sha256-oJGdHNZPn/GaxA4J/D+/sx+DYk/griT2MKVyA9ov2BU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Claims.wasm",
        "name": "System.Security.Claims.5egbkid584.wasm",
        "integrity": "sha256-Q6w0USKwMBHknZXDE8hG9i7QWKjX8rYnyUJn0udkf+8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.uzmwhtezgd.wasm",
        "integrity": "sha256-lt6W8G1t1KSFO/t+DpVc4lKKvXLXWkJ8XBr9ZdTXABI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.1mb5wtiqkz.wasm",
        "integrity": "sha256-CR4uo947E+hVIMj9G3y/qSMnffBRJKZr0J9kSgXuhm8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.nf2lk6d1yg.wasm",
        "integrity": "sha256-k3sDX908GJAWsUggUZ0kx0R/ZlUuVLYkTxG03AExsak=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.6r27jhga9x.wasm",
        "integrity": "sha256-smAtMcGv3yF8N0war5vx5Kol15PYS4Dym1y20P2dUBQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.oodiuolzbh.wasm",
        "integrity": "sha256-OJzUaGvrOu7Lma4Rj9ZmDjOzITaftmAelxKvuykTlNs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.3fso54fh0o.wasm",
        "integrity": "sha256-34rw+ruqpJwO3GjCSHc6nkJjog1YShP25YOCOGoaDWM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.HttpUtility.wasm",
        "name": "System.Web.HttpUtility.qsfmd08jeg.wasm",
        "integrity": "sha256-emZiKYEZ+Ht0CiCaIWHe9aPJqXYI+kwDkg23a3SNKg4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.yvm7yg1lrn.wasm",
        "integrity": "sha256-Yp7HctSyXeSO2zBrOmlpGYVoGYtEPQ5PB/6LRCje3UA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.h672dlq8si.wasm",
        "integrity": "sha256-Jy6UGYpZ7EisdHuEN9DXmPSLDi+kg02bMQcGPkwJYIM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ThrottleDebounce.wasm",
        "name": "ThrottleDebounce.w3nnnu2hkc.wasm",
        "integrity": "sha256-vONRG66VipFAlp0Kj4sQLQ5tvkreqRefDgANCKnr+LU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.wasm",
        "name": "netstandard.qoaoppj9yk.wasm",
        "integrity": "sha256-GnmsToJTr7h9JplUroLCxDYTID4O8R68s5f1szpqX9U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "zxing.wasm",
        "name": "zxing.cslsbo1ipr.wasm",
        "integrity": "sha256-tMfd0yYIsmnNL+xt9XPz0JttM4ydnaDExf6FoFjM0Kg=",
        "cache": "force-cache"
      }
    ],
    "libraryInitializers": [
      {
        "name": "_content/Microsoft.AspNetCore.Components.CustomElements/js/dist/Release/Microsoft.AspNetCore.Components.CustomElements.lib.module.js"
      },
      {
        "name": "_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/Microsoft.DotNet.HotReload.WebAssembly.Browser.99zm1jdh75.lib.module.js"
      }
    ],
    "modulesAfterConfigLoaded": [
      {
        "name": "../_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/Microsoft.DotNet.HotReload.WebAssembly.Browser.99zm1jdh75.lib.module.js"
      }
    ],
    "modulesAfterRuntimeReady": [
      {
        "name": "../_content/Microsoft.AspNetCore.Components.CustomElements/js/dist/Release/Microsoft.AspNetCore.Components.CustomElements.lib.module.js"
      }
    ]
  },
  "debugLevel": 0,
  "linkerEnabled": true,
  "globalizationMode": "sharded",
  "extensions": {
    "blazor": {}
  },
  "runtimeConfig": {
    "runtimeOptions": {
      "configProperties": {
        "Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport": false,
        "Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability": true,
        "System.ComponentModel.DefaultValueAttribute.IsSupported": false,
        "System.ComponentModel.Design.IDesignerHost.IsSupported": false,
        "System.ComponentModel.TypeConverter.EnableUnsafeBinaryFormatterInDesigntimeLicenseContextSerialization": false,
        "System.ComponentModel.TypeDescriptor.IsComObjectDescriptorSupported": false,
        "System.Data.DataSet.XmlSerializationIsSupported": false,
        "System.Diagnostics.Metrics.Meter.IsSupported": false,
        "System.Diagnostics.Tracing.EventSource.IsSupported": false,
        "System.GC.Server": true,
        "System.Globalization.Invariant": false,
        "System.TimeZoneInfo.Invariant": false,
        "System.Linq.Enumerable.IsSizeOptimized": true,
        "System.Net.Http.EnableActivityPropagation": false,
        "System.Net.Http.WasmEnableStreamingResponse": true,
        "System.Net.SocketsHttpHandler.Http3Support": false,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
        "System.Resources.ResourceManager.AllowCustomResourceTypes": false,
        "System.Resources.UseSystemResourceKeys": true,
        "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": true,
        "System.Runtime.InteropServices.BuiltInComInterop.IsSupported": false,
        "System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting": false,
        "System.Runtime.InteropServices.EnableCppCLIHostActivation": false,
        "System.Runtime.InteropServices.Marshalling.EnableGeneratedComInterfaceComImportInterop": false,
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
        "System.StartupHookProvider.IsSupported": false,
        "System.Text.Encoding.EnableUnsafeUTF7Encoding": false,
        "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault": true,
        "System.Threading.Thread.EnableAutoreleasePool": false,
        "Microsoft.AspNetCore.Components.Endpoints.NavigationManager.DisableThrowNavigationException": false
      }
    }
  }
}/*json-end*/);export{gt as default,ft as dotnet,mt as exit};
