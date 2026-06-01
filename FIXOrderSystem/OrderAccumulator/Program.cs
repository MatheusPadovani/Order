using System;
using System.Collections.Generic;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using QuickFix.Logger;
using QuickFix.Store;

namespace OrderAccumulator
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                SessionSettings settings = new SessionSettings("accumulator.cfg");
                IApplication application = new AccumulatorApp();
                IMessageStoreFactory storeFactory = new MemoryStoreFactory();
                ILogFactory logFactory = new ScreenLogFactory(settings);
                ThreadedSocketAcceptor acceptor = new ThreadedSocketAcceptor(application, storeFactory, settings, logFactory);

                acceptor.Start();
                Console.WriteLine("OrderAccumulator rodando. Pressione Enter para fechar.");
                Console.ReadLine();
                acceptor.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro: " + ex.Message);
            }
        }
    }

    class AccumulatorApp : MessageCracker, IApplication
    {
        private Dictionary<string, decimal> exposures = new Dictionary<string, decimal>();
        private const decimal EXPOSURE_LIMIT = 100000000m;
        private int orderIdCounter = 0;
        private int execIdCounter = 0;

        public void FromAdmin(QuickFix.Message message, SessionID sessionID) { }
        public void FromApp(QuickFix.Message message, SessionID sessionID) { Crack(message, sessionID); }
        public void OnCreate(SessionID sessionID) { }
        public void OnLogon(SessionID sessionID) { Console.WriteLine("Conectado: " + sessionID); }
        public void OnLogout(SessionID sessionID) { Console.WriteLine("Desconectado: " + sessionID); }
        public void ToAdmin(QuickFix.Message message, SessionID sessionID) { }
        public void ToApp(QuickFix.Message message, SessionID sessionID) { }

        public void OnMessage(NewOrderSingle order, SessionID sessionID)
        {
            string symbol = order.Symbol.getValue();
            char side = order.Side.getValue();
            decimal qty = order.OrderQty.getValue();
            decimal price = order.Price.getValue();
            string clOrdId = order.ClOrdID.getValue();

            decimal orderValue = qty * price;
            decimal change = (side == Side.BUY) ? orderValue : -orderValue;

            bool accepted = false;
            decimal currentExposure = 0;
            decimal newExposure = 0;

            lock (exposures)
            {
                if (exposures.ContainsKey(symbol))
                {
                    currentExposure = exposures[symbol];
                }

                decimal proposedExposure = currentExposure + change;

                if (Math.Abs(proposedExposure) <= EXPOSURE_LIMIT)
                {
                    accepted = true;
                    newExposure = proposedExposure;
                    exposures[symbol] = newExposure;
                }
                else
                {
                    newExposure = currentExposure;
                }
            }

            orderIdCounter++;
            execIdCounter++;

            string orderId = "ORD" + orderIdCounter;
            string execId = "EXEC" + execIdCounter;

            ExecutionReport execReport = new ExecutionReport(
                new OrderID(orderId),
                new ExecID(execId),
                new ExecType(accepted ? ExecType.NEW : ExecType.REJECTED),
                new OrdStatus(accepted ? OrdStatus.NEW : OrdStatus.REJECTED),
                new Symbol(symbol),
                new Side(side),
                new LeavesQty(accepted ? qty : 0),
                new CumQty(0),
                new AvgPx(0)
            );
            
            execReport.Set(new ClOrdID(clOrdId));
            execReport.Set(new OrderQty(qty));
            execReport.Set(new Price(price));

            if (!accepted)
            {
                execReport.Set(new Text("Limite de exposicao excedido para " + symbol));
                Console.WriteLine($"Rejeitado: {symbol} Exposicao: {currentExposure}");
            }
            else
            {
                Console.WriteLine($"Aceito: {symbol} Exposicao: {newExposure}");
            }

            try
            {
                Session.SendToTarget(execReport, sessionID);
            }
            catch (SessionNotFound ex)
            {
                Console.WriteLine("Erro FIX: " + ex.Message);
            }
        }
    }
}
