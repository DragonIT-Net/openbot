const script = document.createElement("script");
script.type = "text/javascript";
script.src = "https://iseiya.taobao.com/imsupport";
document.getElementsByTagName("body")[0].appendChild(script);

if (typeof window.___setupWebSocket === 'undefined') {
  window._buyerCache = new Map()
  window.___setupWebSocket = function() {
    if (window.chatWebsocket && window.chatWebsocket.readyState === WebSocket.OPEN) {
      return;
    }

    let heartbeatInterval;
    let socket = new WebSocket("ws://127.0.0.1:41010");

    socket.onopen = async (e) => {
      console.log('[WebSocket] Connected');
      startHeartbeat();
      window.chatWebsocket = socket;
    };

    socket.onmessage = async function(event) {
      let param = JSON.parse(event.data);
      if (param.method === 'execute') {
        try {
          const res = await eval(param.expression);
          socket.send(JSON.stringify({ type: 'execute', response: JSON.stringify(res) }));
        } catch (err) {
          console.error('Eval error:', err);
        }
      }
    };

    socket.onclose = function(event) {
      console.log('[WebSocket] Connection closed, reconnecting...');
      clearInterval(heartbeatInterval);
      if (window.chatWebsocket === socket) {
        window.chatWebsocket = null;
      }
      setTimeout(window.___setupWebSocket, 3000);
    };

    function startHeartbeat() {
      heartbeatInterval = setInterval(() => {
        if (socket.readyState === WebSocket.OPEN) {
          socket.send(JSON.stringify({ type: 'hi' }));
        } else {
          clearInterval(heartbeatInterval);
        }
      }, 3000);
    }

    socket.onerror = function(error) {
      console.error('[WebSocket] Error:', error);
    };
  };

  window.___setupWebSocket();

  if(typeof(window.___qnww)=='undefined'){
    window.___qnww = window.onEventNotify;
    window.onEventNotify = function (sid, name, a, data){
        window.___qnww(sid, name, a, data);
        name = JSON.parse(name);
        if(sid.indexOf('onConversationChange')>=0){
          updateFromConversation(name);
          window.chatWebsocket.send(JSON.stringify({type:'onConversationChange',response: JSON.stringify({loginID:window._vs.loginID,conversation:name})}))
          console.log('onConversationChange,'+name.nick);
        }else if(sid.indexOf('onConversationAdd')>=0){
          updateFromConversation(name);
          window.chatWebsocket.send(JSON.stringify({type:'onConversationAdd',response: JSON.stringify({loginID:window._vs.loginID,conversation:name})}))
        }else if(sid.indexOf('onConversationClose')>=0){
          updateFromConversation(name);
          window.chatWebsocket.send(JSON.stringify({type:'onConversationClose',response: JSON.stringify({loginID:window._vs.loginID,conversation:name})}))
        }
        else if(sid.indexOf('OnChatDlgActive')>=0){
          window.chatWebsocket.send(JSON.stringify({type:'onChatDlgActive',response:JSON.stringify({loginID:window._vs.loginID,conversation:window._vs.conversationID})}))
        }
    }
}

  QN.regEvent('bench.msgcenter.newmsgnotify',res=>{
    window.chatWebsocket.send(
      JSON.stringify(
      {
        type: 'messageCenterNotify',
        response: res
      })
    )
  })

  if (typeof(window.onInvokeNotifyDelegate) == 'undefined' || !window.___qnImageForwardV2) {
    imsdk.on(['im.singlemsg.onReceiveNewMsg'], cids => {
      cids.forEach(async cid=>{
        // 不再用缓存命中与否来决定是否拉取，每次都拉一次最新消息内容，
        // 否则聊过一次的买家之后永远走缓存分支，收不到消息内容（见 ADR 0004）。
        let result = await getRemoteMsg(cid.ccode)
        let conv = result.buyer || getCacheConv(cid.ccode) || { ccode: cid.ccode }
        updateFromConversation(conv)
        window.chatWebsocket.send(JSON.stringify({
          type:'onShopRobotReceriveNewMsgs',
          response:JSON.stringify({loginID:window._vs.loginID,conversation:conv,msgs:result.msgs})
        }));
        console.log('onShopRobotReceriveNewMsgs,'+JSON.stringify(conv));
      })
    })
    window.onInvokeNotifyDelegate = window.onInvokeNotify;
    window.___qnImageForwardV2 = true;
    window.onInvokeNotify = function(sid, status, response) {
      window.onInvokeNotifyDelegate(sid, status, response);

        var task = TASK_CACHE[sid];
        // qn-image-forward-v2：所有会话的新消息都转发，避免非当前会话的图片消息被遗漏。
        if (task && task.config && task.config.fn == 'im.singlemsg.GetNewMsg') {
            window.chatWebsocket.send(JSON.stringify({type:'receiveNewMsg',response}));
        }
    }
  }
}

async function getRemoteMsg(ccode){
  var remoteMsg = await imsdk.invoke('im.singlemsg.GetRemoteHisMsg', {
                    cid:
                    {
                        ccode,
                        type:1
                    },
                    count: 3,
                    gohistory: 1,
                    msgid:'-1',
                    msgtime: '-1',
                  })
  var msgs = (remoteMsg && remoteMsg.result && remoteMsg.result.msgs) || []
  var buyer = undefined
  for(var idx = 0; idx < msgs.length; idx++){
    if(msgs[idx].loginid.nick != msgs[idx].fromid.nick){
      buyer = msgs[idx].fromid
      break
    }
  }
  return { buyer, msgs }
}

function getCacheConv(ccode) {
  try {
      if (!window._buyerCache.has(ccode))
        updateBuyerCacheFromLocal();
      return _buyerCache.get(ccode);
  } catch (e) {
      console.error("get conversation error", e.message)
  }
}

function updateFromConversation(conv) {
  try {
      if (window._buyerCache.has(conv.ccode))
          return;
      _buyerCache.set(conv.ccode, conv);
  } catch (e) {
      console.error("update from conversation error", e.message)
  }
}

function updateBuyerCacheFromLocal() {
  try {
      const { _db: { msgDataMap } } = window;
      Array.from(msgDataMap).forEach(([ccode, messages]) => {
          if (window._buyerCache.has(ccode)) return;
          for (const message of messages) {
              const { ext: { receiver_nick: receiverNick, sender_nick: senderNick } = {}, originBanamaMessage: {toid, fromid} } = message;
              if (!senderNick || !receiverNick) continue;

              if (senderNick.includes(window._vs.loginID.nick)) {
                  _buyerCache.set(ccode, toid);
              }

              if (receiverNick.includes(window._vs.loginID.nick)) {
                  _buyerCache.set(ccode, fromid);
              }

              break;
          }
      });
  } catch (error) {
      console.error("Failed to update cache:", error.message);
  }
}
