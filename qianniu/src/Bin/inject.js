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

  // qn-background-message-v3: report background message event/fetch diagnostics to Bot.
  if (typeof(window.onInvokeNotifyDelegate) == 'undefined' || !window.___qnBackgroundMessageV3) {
    const reportBackgroundMessage = function(stage, detail) {
      try {
        if (window.chatWebsocket && window.chatWebsocket.readyState === WebSocket.OPEN) {
          window.chatWebsocket.send(JSON.stringify({
            type: 'backgroundMessageDiagnostic',
            response: JSON.stringify({ stage: stage, detail: detail || {} })
          }));
        }
      } catch (error) {
        console.error('[background-message]', stage, error);
      }
    };
    imsdk.on(['im.singlemsg.onReceiveNewMsg'], cids => {
      cids.forEach(async cid=>{
        try {
        // 不再用缓存命中与否来决定是否拉取，每次都拉一次最新消息内容，
        // 否则聊过一次的买家之后永远走缓存分支，收不到消息内容（见 ADR 0004）。
        reportBackgroundMessage('event', { ccode: cid.ccode });
        let result = await getRemoteMsg(cid.ccode)
        let conv = result.buyer || getCacheConv(cid.ccode) || { ccode: cid.ccode }
        updateFromConversation(conv)
        reportBackgroundMessage('fetched', {
          ccode: cid.ccode,
          buyer: conv.nick || '',
          messageCount: result.msgs.length,
          responseShape: result.responseShape
        });
        window.chatWebsocket.send(JSON.stringify({
          type:'onShopRobotReceriveNewMsgs',
          response:JSON.stringify({loginID:window._vs.loginID,conversation:conv,msgs:result.msgs})
        }));
        console.log('onShopRobotReceriveNewMsgs,'+JSON.stringify(conv));
        } catch (error) {
          reportBackgroundMessage('error', {
            ccode: cid && cid.ccode ? cid.ccode : '',
            message: error && error.message ? error.message : String(error)
          });
          console.error('[background-message] failed to fetch conversation', error);
        }
      })
    })
    window.onInvokeNotifyDelegate = window.onInvokeNotify;
    window.___qnBackgroundMessageV3 = true;
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
  var result = remoteMsg ? remoteMsg.result : null
  if (typeof result === 'string') {
    try {
      result = JSON.parse(result)
    } catch (error) {
      console.error('[background-message] unable to parse GetRemoteHisMsg result', error)
    }
  }
  var messageCandidates = [
    result && result.msgs,
    result && result.result && result.result.msgs,
    result && result.data && result.data.msgs,
    remoteMsg && remoteMsg.msgs,
    remoteMsg && remoteMsg.data && remoteMsg.data.msgs
  ]
  var msgs = []
  for (var candidateIndex = 0; candidateIndex < messageCandidates.length; candidateIndex++) {
    if (Array.isArray(messageCandidates[candidateIndex])) {
      msgs = messageCandidates[candidateIndex]
      break
    }
  }
  var buyer = undefined
  for(var idx = 0; idx < msgs.length; idx++){
    if(msgs[idx] && msgs[idx].loginid && msgs[idx].fromid && msgs[idx].loginid.nick != msgs[idx].fromid.nick){
      buyer = msgs[idx].fromid
      break
    }
  }
  return {
    buyer: buyer,
    msgs: msgs,
    responseShape: {
      topLevelKeys: remoteMsg ? Object.keys(remoteMsg).join(',') : '',
      resultType: typeof result,
      resultKeys: result && typeof result === 'object' ? Object.keys(result).join(',') : ''
    }
  }
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
