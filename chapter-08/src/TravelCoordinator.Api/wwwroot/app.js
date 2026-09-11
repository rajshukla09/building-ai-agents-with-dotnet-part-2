(() => {
  const byId = id => document.getElementById(id);
  let conversationId = null;

  byId('send').addEventListener('click', async () => {
    byId('send').disabled = true;
    byId('status').textContent = 'Discovering and calling remote agent…';
    byId('timeline').innerHTML = '';

    try {
      const response = await fetch('/api/travel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: byId('message').value, conversationId })
      });
      const data = await response.json();
      conversationId = data.conversationId;

      byId('status').textContent = `${data.status} · ${data.durationMs} ms`;
      byId('agentCard').textContent = data.agentCard
        ? JSON.stringify(data.agentCard, null, 2)
        : 'Discovery failed.';
      byId('remoteRequest').textContent = data.remoteRequest || '—';
      byId('remoteResponse').textContent = data.remoteResponse || '—';
      byId('final').textContent = data.finalResponse || data.error || '—';

      for (const event of data.events) {
        const item = document.createElement('li');
        item.textContent = `${event.operation} · ${event.durationMs} ms · ${event.success ? 'success' : 'failure'}${event.detail ? ` — ${event.detail}` : ''}`;
        byId('timeline').append(item);
      }
    } catch (error) {
      byId('status').textContent = 'Failed';
      byId('final').textContent = error.message;
    } finally {
      byId('send').disabled = false;
    }
  });
})();
