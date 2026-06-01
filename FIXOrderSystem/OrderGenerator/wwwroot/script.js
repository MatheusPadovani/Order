document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('orderForm');
    const submitBtn = document.getElementById('submitBtn');
    const resultMessage = document.getElementById('resultMessage');

    form.addEventListener('submit', async (e) => {
        e.preventDefault();

        resultMessage.className = 'result-message hidden';
        submitBtn.disabled = true;
        submitBtn.textContent = 'Processando...';

        const data = {
            symbol: document.getElementById('symbol').value,
            side: document.getElementById('side').value,
            quantity: parseInt(document.getElementById('quantity').value, 10),
            price: parseFloat(document.getElementById('price').value)
        };

        try {
            const response = await fetch('/api/orders', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(data)
            });

            if (!response.ok) {
                throw new Error('Erro na requisição');
            }

            const result = await response.json();
            
            resultMessage.textContent = result.message || (result.success ? 'Ordem aceita com sucesso!' : 'Ordem rejeitada.');
            
        } catch (error) {
            resultMessage.textContent = 'Erro ao conectar com o servidor.';
        } finally {
            submitBtn.disabled = false;
            submitBtn.textContent = 'Enviar Ordem';
            resultMessage.classList.remove('hidden');
        }
    });
});
