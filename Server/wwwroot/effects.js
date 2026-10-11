'use strict';
// Short synthesized cues: no downloads, music loop or external audio dependency.
const gameEffects=(()=>{
    const key='esun10.sound',motion=matchMedia('(prefers-reduced-motion: reduce)');
    let enabled=true,context=null,bus=null,lastWarning='';
    try{enabled=localStorage.getItem(key)!=='off'}catch{}
    const tunes={select:[[440,0,.05]],tick:[[520,0,.09]],start:[[523,0,.12],[659,.12,.12],[784,.24,.22]],correct:[[523,0,.14],[659,.1,.14],[1047,.22,.3]],wrong:[[240,0,.12],[180,.13,.16]],near:[[523,0,.15],[659,.15,.24]],clue:[[740,0,.08],[880,.08,.12]],warning:[[600,0,.09],[600,.18,.09]],finish:[[523,0,.16],[659,.16,.16],[784,.32,.16],[1047,.5,.4]]};
    function update(){const button=document.querySelector('#soundToggle');if(button){button.textContent=enabled?'♪ Sonido':'♪ Silenciado';button.setAttribute('aria-pressed',String(enabled));button.title=enabled?'Silenciar efectos de sonido':'Activar efectos de sonido'}}
    function unlock(){if(!enabled||document.hidden)return;try{context??=new (window.AudioContext||window.webkitAudioContext)();if(!bus){bus=context.createGain();bus.connect(context.destination)}bus.gain.value=1;void context.resume().catch(()=>{})}catch{}}
    function play(name){if(!enabled||document.hidden||context?.state!=='running')return;try{for(const [frequency,delay,duration] of tunes[name]||[]){const oscillator=context.createOscillator(),gain=context.createGain(),at=context.currentTime+delay;oscillator.type='sine';oscillator.frequency.value=frequency;oscillator.connect(gain);gain.connect(bus);gain.gain.setValueAtTime(0,at);gain.gain.linearRampToValueAtTime(.055,at+.008);gain.gain.exponentialRampToValueAtTime(.001,at+duration);oscillator.start(at);oscillator.stop(at+duration+.02);oscillator.onended=()=>{oscillator.disconnect();gain.disconnect()}}}catch{}}
    function toggle(){enabled=!enabled;try{localStorage.setItem(key,enabled?'on':'off')}catch{}if(bus)bus.gain.value=enabled?1:0;if(enabled){unlock();play('select')}update()}
    function animate(selector,name){const element=document.querySelector(selector);if(!element||motion.matches||document.hidden)return;element.classList.remove(name);void element.offsetWidth;element.classList.add(name);element.addEventListener('animationend',()=>element.classList.remove(name),{once:true})}
    function receive(previous,current){
        if(!previous||previous.code!==current.code||previous.version===current.version)return;
        if(current.state==='finished'&&previous.state!=='finished'){play('finish');animate('.finished .title','celebrate');return}
        if(current.state==='playing'&&(previous.state!=='playing'||previous.turnId!==current.turnId)){play('start');return}
        if(current.turnId!==previous.turnId)return;
        if(current.state==='reveal'&&previous.state==='playing'){
            const points=(current.players.find(p=>p.id===current.activePlayerId)?.score||0)-(previous.players.find(p=>p.id===current.activePlayerId)?.score||0);
            play(points===3?'correct':points>0?'near':'wrong');animate('.playing-card',points>0?'celebrate':'card-reveal');animate('#result','notice-in');
        }else if(current.state==='playing'&&current.triedNumbers.length>previous.triedNumbers.length){play('wrong');animate('.playing-card','card-shake');animate('#result','notice-in')}
        else if(current.state==='playing'&&current.clues.length>previous.clues.length)play('clue');
    }
    function tick(room,seconds){const urgent=room.state==='playing'&&seconds>0&&seconds<=10;document.querySelector('#timer')?.classList.toggle('timer-urgent',urgent);if(urgent&&lastWarning!==room.turnId){lastWarning=room.turnId;play('warning')}}
    document.addEventListener('pointerdown',unlock);
    document.addEventListener('keydown',unlock);
    document.addEventListener('visibilitychange',()=>{if(bus)bus.gain.value=document.hidden||!enabled?0:1});
    window.addEventListener('storage',event=>{if(event.key===key){enabled=event.newValue!=='off';if(bus)bus.gain.value=enabled?1:0;update()}});
    update();return {play,toggle,unlock,receive,tick,animate};
})();
