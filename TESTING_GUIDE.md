# Руководство по тестированию UniversityHelper

## Проблема, которая была решена

Приложение `UniversityHelper.Ingestor` зависало после выполнения задачи парсинга, потому что `BackgroundService` не останавливал хост автоматически.

### Решение
Добавлен вызов `IHostApplicationLifetime.StopApplication()` в конце метода `ExecuteAsync` в классе `Worker`. Теперь после завершения парсинга приложение корректно завершает работу.

## Как запустить Ingestor (парсер и индексация)

```powershell
dotnet run --project UniversityHelper.Ingestor/UniversityHelper.Ingestor.csproj
```

### Что должно произойти:
1. ✅ Загружается ONNX модель
2. ✅ Инициализируется Playwright и браузер Chromium
3. ✅ Выполняется навигация на сайт университета
4. ✅ Извлекается текст страницы
5. ✅ Текст разбивается на чанки (~16 чанков для тестовой страницы)
6. ✅ Каждый чанк сохраняется в Qdrant (с генерацией эмбеддинга через ONNX)
7. ✅ Приложение автоматически завершается с сообщением "Worker completed execution. Stopping application..."

### Ожидаемый вывод:
```
info: UniversityHelper.Ingestor.Worker[0]
      Worker started. Target URL: https://urfu.ru/priemurfu/#programs
info: UniversityHelper.Ingestor.Worker[0]
      Initiating scraping...
...
info: UniversityHelper.Ingestor.ScraperService[0]
      Scraping completed successfully.
info: UniversityHelper.Ingestor.Worker[0]
      Scraping finished.
info: UniversityHelper.Ingestor.Worker[0]
      Worker completed execution. Stopping application...
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
```

## Как запустить ChatAPI (RAG-чат)

```powershell
dotnet run --project UniversityHelper.ChatAPI/UniversityHelper.ChatAPI.csproj
```

API будет доступен по адресу: `http://localhost:5000` (или другой порт, указанный в `launchSettings.json`)

### Тестирование через Swagger:
1. Откройте `http://localhost:5000/swagger`
2. Найдите эндпоинт `/ask`
3. Отправьте запрос с вопросом об университете

### Тестирование через curl/PowerShell:
```powershell
Invoke-RestMethod -Uri "http://localhost:5000/ask?question=Какие программы обучения есть в университете?" -Method Post
```

## Конфигурация

### UniversityHelper.Ingestor/appsettings.json
```json
{
  "AiSettings": {
    "QdrantEndpoint": "http://localhost:6333",
    "OnnxModelPath": "C:\\Users\\anton\\RiderProjects\\ConsoleApp16\\onnx-all-MiniLM-L6-v2"
  },
  "ScrapeUrl": "https://urfu.ru/priemurfu/#programs"
}
```

### UniversityHelper.ChatAPI/appsettings.json
```json
{
  "AiSettings": {
    "ChatEndpoint": "https://router.huggingface.co/hf-inference/v1",
    "ChatModel": "HuggingFaceH4/zephyr-7b-beta",
    "QdrantEndpoint": "http://localhost:6333",
    "ApiKey": "hf_...",
    "OnnxModelPath": "C:\\Users\\anton\\RiderProjects\\ConsoleApp16\\onnx-all-MiniLM-L6-v2"
  }
}
```

## Требования

1. **Qdrant должен быть запущен** на порту 6333:
   ```powershell
   docker run -p 6333:6333 qdrant/qdrant
   ```

2. **Playwright браузеры должны быть установлены**:
   ```powershell
   pwsh bin/Debug/net9.0/playwright.ps1 install
   ```
   или
   ```powershell
   dotnet tool install -g Microsoft.Playwright.CLI
   playwright install
   ```

## Устранение неполадок

### Приложение зависает после "Worker completed execution"
- ✅ Исправлено! Добавлен вызов `hostApplicationLifetime.StopApplication()`

### Ошибка "Qdrant connection refused"
- Убедитесь, что Qdrant запущен: `docker ps | Select-String qdrant`
- Проверьте доступность: `Invoke-WebRequest -Uri http://localhost:6333/collections`

### Ошибка "ONNX model not found"
- Проверьте путь в `appsettings.json`
- Убедитесь, что папка `onnx-all-MiniLM-L6-v2` содержит файлы `model.onnx` и `vocab.txt`

### Playwright не может запустить браузер
- Установите браузеры: `playwright install chromium`
- Или используйте PowerShell скрипт из папки bin после сборки проекта

