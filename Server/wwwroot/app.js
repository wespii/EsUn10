'use strict';
const app=document.querySelector('#app'),loginMarkup=app.innerHTML;
let me=null,room=null,roomCode='',selected=0,clueDraft='',timer=null;
let busy=false,connection=null,connecting=false,retryTimer=null,retryDelay=1000;
let refreshing=false,refreshAgain=false,generation=0,serverOffset=0,renderKey='',clueCount=0;
const savedKey='esun10.room';
const serverNow=()=>Date.now()+serverOffset;
const rules='3 vueltas · 90 s · 3 intentos. Acierto: +3. Si tu último intento queda a un número: +1. Resto: 0.';
function savedRoom(){try{return localStorage.getItem(savedKey)||''}catch{return ''}}
function saveRoom(code){try{if(code)localStorage.setItem(savedKey,code);else localStorage.removeItem(savedKey)}catch{}}
function connectionStatus(text){document.querySelector('#connection').textContent=text}
async function api(path,method='GET',body){
    const response=await fetch(path,{method,credentials:'same-origin',signal:AbortSignal.timeout(12000),headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});
    const text=await response.text();let data;try{data=text?JSON.parse(text):{}}catch{data={error:text}}
    if(!response.ok){const error=Error(response.status===429?'Demasiadas acciones. Espera un momento.':data.error||data.title||text||'No se pudo conectar.');error.status=response.status;throw error}return data;
}
    function login(){location.href='/auth/discord'}function esc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}function avatar(p){return p.avatarUrl?`<img src="${esc(p.avatarUrl)}" alt="">`:esc((p.displayName||'?').slice(0,1).toUpperCase())}function showError(e){document.querySelector('#error')?.replaceChildren(document.createTextNode(e.message||String(e)))}
    function renderHome(){app.className='';clearInterval(timer);document.querySelector('#profile').textContent=me?`Hola, ${me.displayName}`:'';app.innerHTML=`<div class="center"><div class="eyebrow">Qué bueno verte, ${esc(me.displayName)}</div><h1 class="title">Junta a tu<br>gente.</h1><p class="subtitle">Crea una mesa privada o únete con el código de quien repartió las cartas.</p><div class="hero-card"><b>10</b></div><div id="error" class="error"></div><div class="actions"><button class="button" onclick="createRoom()">CREAR MESA　→</button></div><div class="joinline"><input id="joinCode" class="input" maxlength="6" placeholder="CÓDIGO DE MESA"><button class="button secondary" onclick="joinRoom()">UNIRME</button></div><p class="note">Comparte el código con 1–3 amigos. La llamada de voz puede ser por Discord.</p></div>`}

    function renderLobby(){if(!room)return;app.className='lobby';const readyCount=room.players.filter(p=>p.ready).length;app.innerHTML=`<div class="center"><div class="eyebrow">La mesa está casi lista</div><h1 class="title">Reúne al grupo.</h1><p class="subtitle">Una carta para adivinar. Muchas versiones de la historia.</p><div class="invite-code"><label class="roomcode">CÓDIGO <input id="inviteCode" aria-label="Código de la mesa" value="${esc(room.code)}" readonly onclick="this.select()"></label><button id="copyCode" class="copy-code" onclick="copyRoomCode()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V4a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h4"/></svg><span>Copiar código</span></button></div><p id="copyStatus" class="note copy-status" role="status" aria-live="polite"></p></div><div id="players" class="players">${room.players.map(p=>`<div class="player"><div class="avatar">${avatar(p)}</div><div><b>${esc(p.displayName)}</b><small>${p.id===room.hostId?'ANFITRIÓN · ':''}${!p.online?'RECONECTANDO…':p.ready?'LISTO':'ESPERANDO'}</small></div></div>`).join('')}</div><div id="error" class="error" role="alert"></div><p class="note center">${room.players.length<2?'Invita al menos a un amigo para empezar.':`Listos: ${readyCount} de ${room.players.length}. La partida empieza 10 segundos después de que todos estén listos.`}</p><div class="countdown" id="countdown" role="status"><strong id="countdownNumber"></strong><span>¡Empezamos en breve!</span></div><div class="center"><button class="sound-toggle" id="soundToggle" onclick="toggleSound()"></button></div><div class="actions"><button class="button secondary" onclick="ready(this)">${room.players.find(p=>p.id===me.id)?.ready?'YA NO ESTOY LISTO':'ESTOY LISTO'}</button><button class="button dark" onclick="leave()">SALIR</button></div><p class="note center">${room.players.length}/4 jugadores · mínimo 2 · comparte el código para invitar</p>`;updateSoundButton();countdownClock()}
    async function copyRoomCode(){const code=room?.code;const button=document.querySelector('#copyCode');const status=document.querySelector('#copyStatus');if(!code||!button)return;try{await navigator.clipboard.writeText(code);if(!button.isConnected)return;button.querySelector('span').textContent='✓ Copiado';status.textContent='Código copiado. Pégalo en el chat de tus amigos.';}catch{const input=document.querySelector('#inviteCode');input?.focus();input?.select();if(status.isConnected)status.textContent='Seleccionamos el código. Usa Copiar o Ctrl+C / ⌘C para compartirlo.';}}

    let audioContext=null,soundEnabled=true,lastCountdownKey='',lastTick=-1;
    function unlockAudio(){if(!soundEnabled)return;try{audioContext??=new (window.AudioContext||window.webkitAudioContext)();audioContext.resume().catch(()=>{})}catch{}}
    function beep(final=false){if(!soundEnabled||audioContext?.state!=='running')return;const oscillator=audioContext.createOscillator(),gain=audioContext.createGain(),now=audioContext.currentTime;oscillator.connect(gain);gain.connect(audioContext.destination);oscillator.frequency.value=final?880:520;gain.gain.setValueAtTime(0,now);gain.gain.linearRampToValueAtTime(.12,now+.01);gain.gain.exponentialRampToValueAtTime(.001,now+(final?.45:.12));oscillator.start(now);oscillator.stop(now+(final?.5:.15))}
    function updateSoundButton(){const b=document.querySelector('#soundToggle');if(b){b.textContent=soundEnabled?'Sonido: activado':'Sonido: silenciado';b.setAttribute('aria-pressed',String(soundEnabled))}}
    function toggleSound(){soundEnabled=!soundEnabled;if(soundEnabled)unlockAudio();updateSoundButton()}
    function countdownClock(){const box=document.querySelector('#countdown');if(!box)return;box.hidden=!room?.countdownEndsAt;box.style.display=room?.countdownEndsAt?'flex':'none';if(!room?.countdownEndsAt){lastCountdownKey='';lastTick=-1;return}if(lastCountdownKey!==room.countdownEndsAt){lastCountdownKey=room.countdownEndsAt;lastTick=-1}const n=Math.max(0,Math.ceil((Date.parse(room.countdownEndsAt)-serverNow())/1000));document.querySelector('#countdownNumber').textContent=n;if(n!==lastTick){lastTick=n;beep(n===0)}}
    document.addEventListener('pointerdown',unlockAudio,{once:true});
    document.addEventListener('keydown',unlockAudio,{once:true});

function receive(updated){
    if(!updated||updated.code!==roomCode||(room&&updated.version<room.version))return;
    serverOffset=Date.parse(updated.serverTime)-Date.now();
    const changed=!room||updated.version!==room.version;
    if(room?.turnId!==updated.turnId){selected=0;clueDraft='';}
    room=updated;
    if(changed)renderRoom();
}
function renderRoom(){
    const key=`${room.state}:${room.turnId}:${room.viewerIsGuesser}`;
    if(room.state==='lobby'){renderLobby();renderKey=key;return}
    if(room.state==='finished'){renderFinished();renderKey=key;return}
    if(key!==renderKey){renderKey=key;buildGame()}
    patchGame();
}
function scores(){return room.players.map(p=>`<div class="score"><span>${esc(p.displayName)} ${p.id===room.activePlayerId?'✦':''} <small>${p.left?'Salió':p.online?'':'Reconectando…'}</small></span><b>${p.score} pts</b></div>`).join('')}
function buildGame(){
    clearInterval(timer);app.className='game';
    if(lastCountdownKey){if(lastTick!==0)beep(true);lastCountdownKey='';lastTick=-1}
    const guesser=room.viewerIsGuesser,reveal=room.state==='reveal';
    app.innerHTML=`<div class="game-heading"><span class="eyebrow" id="roundLabel"></span><button class="sound-toggle" onclick="leave()">Salir de la mesa</button></div>
    <div class="layout"><section><div class="table-card"><div class="timer" id="timer"></div><div class="playing-card"><div class="label" id="cardLabel"></div><div class="num" id="cardNumber"></div><div class="tag" id="cardTag"></div></div></div><div id="error" class="error" role="alert"></div><div class="result" id="result" hidden></div></section>
    <aside><section class="side scoreboard"><h3>La mesa</h3><div class="scores" id="scores"></div></section><section class="side interaction"><h3>${reveal?'La carta revelada':guesser?'Escucha las pistas':'Es un 10, pero…'}</h3><div class="clues" id="clues" aria-live="polite"></div>
    ${reveal?`<p class="note" id="revealNote">El siguiente turno empieza en unos segundos.</p><div id="rating" class="rating" hidden><span>¿Las ayudas encajaban?</span><button onclick="rateHints(true)">Sí</button><button onclick="rateHints(false)">Confusas</button></div>`:guesser?`<div class="note" id="attempts"></div><div class="guessgrid">${Array.from({length:10},(_,i)=>`<button class="numbtn" data-number="${i+1}" onclick="choose(${i+1})">${i+1}</button>`).join('')}</div><button id="guessButton" class="button" onclick="guess()">FIJAR MI NÚMERO</button>`:`<input id="clueText" class="input" aria-label="Escribe tu pista" maxlength="120" placeholder="...pero siempre llega tarde" value="${esc(clueDraft)}" oninput="clueDraft=this.value" onkeydown="if(event.key==='Enter')sendClue()"><button id="sendClue" class="button secondary" onclick="sendClue()">ENVIAR PISTA</button><button id="hintButton" class="hint-button" onclick="hint()">✦ DAME UNA PISTA</button><p id="hintStatus" class="note"></p>`}
    <p class="note rules">${rules}</p></section></aside></div>`;
    clueCount=0;
}
function patchGame(){
    document.querySelector('#profile').textContent=me.displayName;
    document.querySelector('#roundLabel').textContent=`Vuelta ${room.round} de 3 · ${room.players.length} jugadores`;
    document.querySelector('#scores').innerHTML=scores();
    document.querySelector('#cardLabel').textContent=`LA CARTA DE ${room.players.find(p=>p.id===room.activePlayerId)?.displayName||''}`;
    document.querySelector('#cardNumber').textContent=room.secretNumber??'?';
    document.querySelector('#cardTag').textContent=room.state==='reveal'?'Un momento para celebrar…':room.viewerIsGuesser?'Tu número es secreto. Escucha al grupo.':'Inventa una situación que encaje con esta carta.';
    const result=document.querySelector('#result');result.hidden=!room.lastResult;result.textContent=room.lastResult;
    const list=document.querySelector('#clues');
    if(clueCount!==room.clues.length){
        const atBottom=list.scrollHeight-list.scrollTop-list.clientHeight<30;
        if(clueCount===0)list.replaceChildren();
        for(const text of room.clues.slice(clueCount)){const item=document.createElement('div');item.className='clue';item.textContent=text;list.append(item)}
        clueCount=room.clues.length;if(atBottom)list.scrollTop=list.scrollHeight;
    }
    if(!clueCount&&!list.childElementCount){const note=document.createElement('div');note.className='note';note.textContent='Todavía no hay pistas. ¡Rompe el hielo!';list.append(note)}
    if(room.state==='reveal'){document.querySelector('#rating').hidden=!room.canRateHints;}
    else if(room.viewerIsGuesser){
        document.querySelector('#attempts').textContent=`TE QUEDAN ${room.guessesRemaining} INTENTOS`;
        document.querySelectorAll('[data-number]').forEach(button=>{const n=Number(button.dataset.number);button.disabled=busy||room.triedNumbers.includes(n);button.classList.toggle('selected',n===selected);button.classList.toggle('tried',room.triedNumbers.includes(n));button.setAttribute('aria-label',`${n}${room.triedNumbers.includes(n)?', ya intentado':''}`)});
        document.querySelector('#guessButton').disabled=busy||!selected||room.triedNumbers.includes(selected);
    }else{document.querySelector('#sendClue').disabled=busy;}
    uiTick();
}
function renderFinished(){
    app.className='finished';const ranked=[...room.players].filter(p=>!p.left).sort((a,b)=>b.score-a.score);
    const leaders=ranked.filter(p=>p.score===ranked[0]?.score).map(p=>p.displayName).join(' y ');
    app.innerHTML=`<div class="center"><div class="eyebrow">Fin de la partida</div><h1 class="title">${ranked.length?esc(leaders):'Gracias por jugar'}</h1><p class="subtitle">${esc(room.lastResult)}</p></div><div class="players">${ranked.map((p,i)=>`<div class="player"><b>${ranked.filter(other=>other.score>p.score).length+1}.</b><div class="avatar">${avatar(p)}</div><div>${esc(p.displayName)}<small>${p.score} puntos${p.online?'':' · desconectado'}</small></div></div>`).join('')}</div><div id="error" class="error" role="alert"></div><div class="actions">${room.hostId===me.id?'<button class="button" onclick="rematch()">REVANCHA</button>':'<p class="note">El anfitrión puede preparar la revancha.</p>'}<button class="button dark" onclick="leave()">SALIR</button></div><p class="note center">La revancha vuelve al lobby: todos deben marcarse listos.</p>`;
}
function uiTick(){
    if(!room)return;
    if(room.state==='lobby'){countdownClock();return}
    const until=room.state==='reveal'?room.revealEndsAt:room.deadline;
    const seconds=Math.max(0,Math.ceil((Date.parse(until)-serverNow())/1000));
    const clock=document.querySelector('#timer');if(clock)clock.textContent=room.state==='reveal'?`Siguiente · ${seconds}s`:`◷ ${Math.floor(seconds/60)}:${String(seconds%60).padStart(2,'0')}`;
    const hint=document.querySelector('#hintButton');if(hint){const wait=room.nextHintAt?Math.max(0,Math.ceil((Date.parse(room.nextHintAt)-serverNow())/1000)):0;hint.disabled=busy||room.hintsRemaining===0||wait>0;document.querySelector('#hintStatus').textContent=room.hintsRemaining===0?'Ya usasteis las tres ayudas. Ahora le toca al grupo.':wait?`Otra ayuda en ${wait}s · quedan ${room.hintsRemaining}`:`Quedan ${room.hintsRemaining} ayudas para este turno.`}
}
async function action(path,body={},onSuccess){
    if(busy||!roomCode)return;busy=true;const code=roomCode,epoch=generation;
    if(room.state==='playing')patchGame();
    try{const response=await api(`/rooms/${code}/${path}`,'POST',body);if(epoch!==generation||code!==roomCode)return;onSuccess?.();receive(response.room||response)}
    catch(error){if(epoch===generation)showError(error.status===401?'La sesión terminó. Recarga para volver a entrar.':error)}
    finally{busy=false;if(room&&roomCode===code&&room.state==='playing')patchGame()}
}
function choose(n){if(busy||room.triedNumbers.includes(n))return;selected=n;patchGame()}
function guess(){if(!selected)return;return action('guess',{number:selected,turnId:room.turnId,requestId:crypto.randomUUID()},()=>{selected=0})}
function sendClue(){const input=document.querySelector('#clueText');if(!input?.value.trim())return;return action('clue',{text:input.value,turnId:room.turnId},()=>{clueDraft='';input.value=''})}
function hint(){return action('hint',{turnId:room.turnId})}
function rateHints(helpful){return action('hint-rating',{turnId:room.turnId,helpful})}
function rematch(){return action('rematch')}
function ready(){unlockAudio();return action('ready')}
async function openRoom(updated){
    generation++;roomCode=updated.code;room=null;renderKey='';saveRoom(roomCode);receive(updated);
    await connect();if(connection?.state==='Connected')await connection.invoke('JoinRoom',roomCode);await refreshRoom();
}
async function createRoom(){if(busy)return;busy=true;try{await openRoom(await api('/rooms','POST',{}))}catch(e){showError(e)}finally{busy=false}}
async function joinRoom(){if(busy)return;busy=true;try{const code=document.querySelector('#joinCode').value.trim();await openRoom(await api(`/rooms/${encodeURIComponent(code)}/join`,'POST',{}))}catch(e){showError(e)}finally{busy=false}}
async function leave(){
    if(busy||!roomCode)return;busy=true;
    try{await api(`/rooms/${roomCode}/leave`,'POST',{});await clearRoom();renderHome()}catch(e){if(e.status===404){await clearRoom();renderHome()}else showError(e)}finally{busy=false}
}
async function clearRoom(){generation++;room=null;roomCode='';renderKey='';saveRoom('');if(connection?.state==='Connected')await connection.invoke('LeaveRoom').catch(()=>{});connectionStatus('')}
async function logout(){
    if(busy)return;busy=true;
    try{if(roomCode)await api(`/rooms/${roomCode}/leave`,'POST',{}).catch(()=>{});await api('/auth/logout','POST',{});await clearRoom();await connection?.stop();location.reload()}catch(e){showError(e)}finally{busy=false}
}
async function refreshRoom(){
    if(!roomCode)return;if(refreshing){refreshAgain=true;return}refreshing=true;
    const code=roomCode,epoch=generation;
    try{const updated=await api(`/rooms/${code}`);if(epoch===generation&&code===roomCode){receive(updated);retryDelay=1000;connectionStatus(connection?.state==='Connected'?'En línea':'Reconectando…')}}
    catch(e){if(epoch!==generation)return;if(e.status===404){await clearRoom();renderHome();showError('Esta mesa terminó o caducó. Si el servidor se reinició, crea una mesa nueva. Tu sesión sigue iniciada.')}else if(e.status===401){await clearRoom();me=null;app.className='';app.innerHTML=loginMarkup;document.querySelector('#logout').hidden=true;showError('Vuelve a vincular Discord para continuar.')}else{connectionStatus('Reconectando…');scheduleRetry()}}
    finally{refreshing=false;if(refreshAgain){refreshAgain=false;void refreshRoom()}}
}
function scheduleRetry(){if(retryTimer||!roomCode)return;retryTimer=setTimeout(()=>{retryTimer=null;void connect();void refreshRoom()},retryDelay);retryDelay=Math.min(30000,retryDelay*2)}
async function connect(){
    if(connecting||!me||!roomCode)return;
    if(!window.signalR){connectionStatus('Reconectando…');scheduleRetry();return}
    if(!connection){
        connection=new signalR.HubConnectionBuilder().withUrl('/realtime').withAutomaticReconnect([0,2000,5000,10000,30000]).configureLogging(signalR.LogLevel.Error).build();
        connection.on('roomChanged',code=>{if(code===roomCode)void refreshRoom()});
        connection.onreconnecting(()=>connectionStatus('Reconectando…'));
        connection.onreconnected(async()=>{if(roomCode){await connection.invoke('JoinRoom',roomCode);await refreshRoom()}});
        connection.onclose(()=>{if(roomCode){connectionStatus('Reconectando…');scheduleRetry()}});
    }
    if(connection.state!=='Disconnected')return;
    connecting=true;
    try{await connection.start();if(roomCode){await connection.invoke('JoinRoom',roomCode);await refreshRoom()}}
    catch{scheduleRetry()}finally{connecting=false}
}
setInterval(uiTick,250);
setInterval(()=>{if(roomCode&&connection?.state==='Connected')connection.invoke('Pulse',roomCode).catch(()=>void refreshRoom())},10000);
setInterval(()=>{if(roomCode)void refreshRoom()},30000);
window.addEventListener('online',()=>{void connect();void refreshRoom()});
document.addEventListener('visibilitychange',()=>{if(!document.hidden){void connect();void refreshRoom()}});
(async()=>{
    try{
        me=await api('/auth/me');document.querySelector('#logout').hidden=false;renderHome();
        const code=savedRoom();if(code){roomCode=code;await refreshRoom();if(room)await connect()}
    }catch(e){document.querySelector('#profile').textContent='';if(e.status!==401)showError('No se pudo conectar. Comprueba tu conexión y recarga la página.')}
})();
