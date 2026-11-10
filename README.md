# LMS Platform — учебная платформа курсов

Система управления обучением на **ASP.NET Core 8 Web API + MongoDB** с JWT-аутентификацией,
ролями, курсами, заданиями со статусами «решено / не решено», зачислениями, прогрессом,
уведомлениями и простым фронтендом на чистом JavaScript.

---

## Роли

| Роль | Возможности |
| --- | --- |
| **Администратор (`Admin`)** | Пользователи и роли, ручное добавление в БД, зачисления и прогресс, модерация курсов (одобрить/отклонить), статистика |
| **Преподаватель (`Teacher`)** | Создание и редактирование курсов, теория, видео, презентации, задания, проверка решений, отправка курса на проверку |
| **Студент (`Student`)** | Каталог опубликованных курсов, запись на курс, просмотр материалов, отправка решений, статусы заданий и процент прохождения |

### Распределение разработки

- **Вова**: JWT-аутентификация, регистрация/вход, уведомления.
- **Ваня**: MongoDB, модели и структура курсов, задания и решения, администрирование, роли, зачисления и прогресс, фронтенд.

---

## Стек

- **Backend**: C# / ASP.NET Core 8 Minimal API
- **БД**: MongoDB (драйвер `MongoDB.Driver`)
- **Аутентификация**: JWT Bearer + BCrypt
- **Frontend**: HTML + CSS + vanilla JavaScript (без сборки), раздаётся из `src/wwwroot`
- **Документация API**: Swagger UI
- **Docker**: `Dockerfile` + `docker-compose.yml` (API + MongoDB)

---

## Быстрый запуск

### Вариант 1. Docker Compose (рекомендуется)

```bash
docker compose up --build
```

- API и фронтенд: <http://localhost:8080>
- Swagger: <http://localhost:8080/swagger>
- MongoDB: `localhost:27017` (контейнер `mongo`, том `mongo-data`)

### Вариант 2. Локально

1. Запустите MongoDB на `localhost:27017` (локально или контейнером):

   ```bash
   docker run -d --name lms-mongo -p 27017:27017 mongo:7
   ```

2. Запустите API:

   ```bash
   cd src
   dotnet run
   ```

3. Откройте <http://localhost:5221> (порт из `launchSettings.json`) или Swagger `/swagger`.

Строка подключения и имя БД задаются в `src/appsettings.json`:

```json
"MongoDb": {
  "ConnectionString": "mongodb://localhost:27017",
  "DatabaseName": "LmsPlatform"
}
```

Переменные окружения Docker Compose переопределяют их через `MongoDb__ConnectionString` и
`MongoDb__DatabaseName`.

---

## Демо-доступ (создаётся автоматически)

| Роль | Email | Пароль |
| --- | --- | --- |
| Администратор | `admin@lms.local` | `Admin123!` |
| Преподаватель | `teacher@lms.local` | `Teacher123!` |
| Студент | `student@lms.local` | `Student123!` |

При первом запуске создаются:

- системные роли `Admin`, `Teacher`, `Student` в коллекции `roles`;
- администратор;
- демо-преподаватель, демо-студент и опубликованный курс «Основы C# и ASP.NET Core» с заданиями.

Отключить демо-данные: `"Seed:DemoData": false` в `appsettings.json` или
`Seed__DemoData=false` в окружении. Администратор создаётся всегда, если его ещё нет.

---

## Структура проекта

```text
abobys/
├── src/
│   ├── Program.cs                     # JWT, Swagger, статика и запуск приложения
│   ├── Models.cs                      # модели, enum-ы и request-классы
│   ├── Api.cs                         # Db, сидер и все эндпоинты
│   ├── wwwroot/                       # фронтенд
│   │   ├── index.html
│   │   ├── login.html
│   │   ├── courses.html
│   │   ├── course.html
│   │   ├── admin.html
│   │   ├── css/site.css
│   │   └── js/app.js
│   ├── appsettings.json
│   └── LmsPlatform.csproj
├── docker-compose.yml
├── Dockerfile
├── .env
└── README.md
```

---

## Коллекции MongoDB

| Коллекция | Назначение |
| --- | --- |
| `users` | Пользователи (ФИО, email, BCrypt-хэш, роль) |
| `roles` | Роли и их права (системные создаются сидером) |
| `courses` | Курсы; задания хранятся вложенно в `assignments` |
| `enrollments` | Записи на курсы + `progressPercentage` |
| `submissions` | Решения студентов, одна запись на пару (задание, студент) |
| `notifications` | Уведомления пользователей, дедлайны |

Индексы создаются при старте: уникальный email, уникальная пара `userId + courseId` для
зачислений и уникальная пара `assignmentId + studentId` для решений.

---

## Основные эндпоинты

### Аутентификация

| Метод | Путь | Доступ | Описание |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | публично | Регистрация студента или преподавателя |
| `POST` | `/api/auth/login` | публично | Вход, возвращает JWT |
| `GET` | `/api/auth/me` | авторизован | Текущий пользователь |

Регистрация администратора через публичный эндпоинт запрещена — только через админ-панель или сидер.

### Курсы

| Метод | Путь | Доступ | Описание |
| --- | --- | --- | --- |
| `GET` | `/api/courses?status=&search=&mine=` | авторизован | Каталог с учётом роли; `mine=true` — только свои курсы |
| `GET` | `/api/courses/{id}` | авторизован | Детали курса, задания, решения и прогресс студента |
| `POST` | `/api/courses` | Teacher/Admin | Создать черновик |
| `PUT` | `/api/courses/{id}` | автор/Admin | Редактировать |
| `DELETE` | `/api/courses/{id}` | автор/Admin | Удалить курс и связанные данные |
| `POST` | `/api/courses/{id}/submit` | автор/Admin | Отправить на проверку |
| `POST` | `/api/courses/{id}/approve` | Admin | Опубликовать |
| `POST` | `/api/courses/{id}/reject` | Admin | Отклонить с комментарием |
| `POST` | `/api/courses/{id}/enroll` | Student | Записаться |
| `DELETE` | `/api/courses/{id}/enroll` | Student | Отписаться |
| `GET` | `/api/courses/{id}/students` | автор/Admin | Студенты курса и прогресс |

### Задания и решения

| Метод | Путь | Доступ | Описание |
| --- | --- | --- | --- |
| `POST` | `/api/courses/{courseId}/assignments` | автор/Admin | Добавить задание |
| `PUT` | `/api/courses/{courseId}/assignments/{assignmentId}` | автор/Admin | Изменить задание |
| `DELETE` | `/api/courses/{courseId}/assignments/{assignmentId}` | автор/Admin | Удалить задание |
| `POST` | `/api/courses/{courseId}/assignments/{assignmentId}/submit` | Student | Отправить решение |
| `GET` | `/api/courses/{courseId}/assignments/{assignmentId}/submissions` | автор/Admin | Решения студентов |
| `PUT` | `/api/courses/{courseId}/assignments/{assignmentId}/submissions/{submissionId}` | автор/Admin | Поставить «решено / не решено» и комментарий |

### Администрирование

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/api/admin/users?role=&search=` | Список пользователей |
| `POST` | `/api/admin/users` | Добавить пользователя в БД |
| `PUT` | `/api/admin/users/{id}` | Изменить ФИО/email/пароль |
| `PUT` | `/api/admin/users/{id}/role` | Изменить роль |
| `DELETE` | `/api/admin/users/{id}` | Удалить пользователя |
| `GET` | `/api/admin/users/{id}/enrollments` | Зачисления пользователя |
| `POST` | `/api/admin/users/{id}/enrollments` | Записать на курс |
| `PUT` | `/api/admin/users/{id}/enrollments/{courseId}` | Задать процент прохождения |
| `DELETE` | `/api/admin/users/{id}/enrollments/{courseId}` | Удалить зачисление |
| `GET` | `/api/admin/roles` | Роли из БД |
| `GET` | `/api/admin/stats` | Статистика |

### Уведомления

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/api/notifications` | Уведомления пользователя |
| `GET` | `/api/notifications/unread-count` | Количество непрочитанных |
| `PUT` | `/api/notifications/{id}/read` | Отметить прочитанным |
| `PUT` | `/api/notifications/read-all` | Отметить все прочитанными |

Уведомления создаются при регистрации, зачислении, добавлении задания с дедлайном,
отправке курса на проверку, публикации/отклонении курса и проверке решения.

---

## Фронтенд

Фронтенд лежит в `src/wwwroot` и раздаётся тем же приложением:

| Страница | Назначение |
| --- | --- |
| `/` | Лендинг, демо-доступ |
| `/login.html` | Вход и регистрация студента/преподавателя |
| `/courses.html` | Каталог, поиск, фильтры, создание курса, модерация |
| `/course.html?id=...` | Теория, материалы, задания, решения, проверка, студенты курса |
| `/admin.html` | Пользователи, роли, зачисления, статистика |

JWT хранится в `localStorage`, запросы идут на тот же origin (`/api`).

---

## Прогресс и статусы

- Прогресс курса = `решённые задания / все задания курса * 100`.
- Задание считается решённым, когда преподаватель или администратор поставил статус `Solved`.
- После повторной отправки решения статус сбрасывается на `NotSolved` до проверки.
- Администратор может вручную изменить процент прохождения в зачислении.

---

## Особенности реализации

- Бэкенд собран на Minimal API: `Program.cs`, `Models.cs` и `Api.cs`.
- Все эндпоинты находятся в группе `/api`.
- Enum-ы передаются в JSON строками (`Student`, `Published`, `Solved`).
- Доступ проверяется стандартными средствами ASP.NET Core: JWT Bearer и роли.
- Фронтенд не требует сборки и использует `fetch` с токеном из `localStorage`.
