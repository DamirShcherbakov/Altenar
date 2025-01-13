using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

// В .NET есть 3 способа работы с коллекциями в асинхронном/многопоточном коде 
//     (На примере словаря)
//
// 1 Dictionary + примитив синхронизации, например ReaderWriterLock.
//              Mожно использовать в тех случаях, если запись в словарь происходит редко.
//              ReaderWriterLock будет закрывать чтение и запись в словарь. В таком случае только один поток сможет писать в словарь, а читать смогут n потоков.
//              При этом, так же необходимо будет закрыть методы Count, IsEmpty, GetEnumerator, ToArray как Read блок, если необходима блокировка на этом функционале.
//              У Dictionary будет Lazy конструкция на чтение - это может иметь как свои плюсы, так и минусы.
//              Использование Dictionary + ReaderWriterLock позволяет вручную реализовать подобие ConcurrentDictionary,
//              но, например, без полной блокировки коллекции на методах, использующих всю коллекцию (наподобие Count).
//              Так же можно не закрывать блокировкой методы чтения, но закрыть методы записи. 
// 2 ConcurrentDictionary.
//              Используется в большинстве случаев. Позволяет писать и читать из n потоков одновременно. 
// 3 ImmutableDictionary.
//              Можем применить в том случае, если содержимое словаря заранее известно и не будет изменяться.
//              Для поиска и выборки использует сильно меньше ресурсов, в отличие от ConcurrentDictionary.
//
// Решил так же добавить несколько инструментов, которые так же приходилось использовать:
// 4 BlockingCollection.
//              Потокобезопасная коллекция, используется в шаблоне писатель - читатель.
//              В случае, если читатель обращается к коллекции, а в ней нет значений, читатель переходит в режим ожидания пополнения коллекции.
//              Можно использовать для организации очереди задач или пула сообщений.
// 5 Channels.
//              Обертка потокобезопасной коллекции, используемой так же для реализации шаблона производитель - потребитель.
//              В отличие от BlockingCollection является асинхронной для производителей/потребителей. 
//              Позволяет конфигурировать разные стратегии по наполнению и потреблению, в том числе в отношении потоков.
//
//     Нужно придумать пример, когда каждый подход наиболее удобен или эффективен для практической задачи
//     Нужно запустить код дома и рассказать, почему оно работает так, как работает
// Describe the output (Console App .Net 8. )

public class TestTask
{
    private readonly ITestOutputHelper _testOutputHelper;

    public TestTask(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    const int PARTICIPANTS = 4;

    [Fact]
    public void Handle()
    {
        var collection = new ConcurrentDictionary<object, int>();

        using var barrier = new Barrier(PARTICIPANTS);

        var addCallCount = 0;
        var updateCallCount = 0;

        object key = new();

        var threads = Enumerable.Range(0, PARTICIPANTS)
            .Select(
                _ =>
                {
                    var thread = new Thread(AddOrUpdate);
                    thread.Start();
                    return thread;
                })
            .ToList();

        threads.ForEach(thread => thread.Join());

        _testOutputHelper.WriteLine( "Количество попыток ДОБАВИТЬ значение в словарь: " + addCallCount.ToString());
        _testOutputHelper.WriteLine("Количество попыток ОБНОВИТЬ значение в словаре: " + updateCallCount.ToString());
        
        _testOutputHelper.WriteLine("Словарь: " + string.Join(", ", collection.Select(x => x.Value.ToString())));
        
        int Add(object _)
        {
            Thread.Sleep(
                TimeSpan.FromSeconds(1));

            // Конкурентно итерируем переменную 
            Interlocked.Increment(ref addCallCount);
            
            _testOutputHelper.WriteLine("Идентификатор потока, совершивего попытку ДОБАВЛЕНИЯ: " 
                                        + Environment.CurrentManagedThreadId.ToString());

            return 1;
        }

        int Update(object _, int oldValue)
        {
            Thread.Sleep(
                TimeSpan.FromSeconds(1));

            // Конкурентно итерируем переменную 
            Interlocked.Increment(ref updateCallCount);
            
            _testOutputHelper.WriteLine("Идентификатор потока, совершивего попытку ОБНОВЛЕНИЯ: " 
                                        + Environment.CurrentManagedThreadId.ToString());
            
            return oldValue + 1;
        }

        void AddOrUpdate()
        {
            // Дожидаемся, когда все 4 созданные Task дойдут до этой строки
            barrier.SignalAndWait();
            // Под капотом будут вызвыны TryGetValue и далее TryAdd или TryUpdate
            // При параллельном обращении к словарю по одинаковому ключу по spin-white алгоритму (while(AddOrUpdateTrue))
            //      будет происходить повторное обращение до того момента,
            //      пока не будет выполнено добавление или обновление
            collection.AddOrUpdate(key, Add, Update);
        }
    }
}
