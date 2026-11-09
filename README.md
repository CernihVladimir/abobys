# LMS Platform - Backend Service (ASP.NET Core Web API)

Система управления обучением (LMS Platform) на C# ASP.NET Core Web API 8.0 с аутентификацией JWT, распределением ролей и поддержкой Docker.

---

## 👥 Роли и структура проекта

### Роли в системе:
- **Преподаватель (`Teacher`)**: создание и редактирование курсов, управление теоретическим и практическим материалом.
- **Студент (`Student`)**: прохождение курсов, отслеживание процента выполнения.
- **Администратор (`Admin`)**: управление пользователями, правами/ролями, проверка курсов.

### Распределение разработки:
- **Вова (Бэкендер)**: Аутентификация (JWT), регистрация, управление токенами, уведомления (Notifications).
- **Ваня (Бэкендер)**: База данных (EF Core), структура курсов (Course), административная панель (Admin).

---

## 📁 Структура каталогов

```text
lms-platform/
│
├── src/
│   ├── Controllers/
│   │   ├── AuthController.cs         # (Вова) Регистрация, вторые/первые входы (JWT)
│   │   ├── AdminController.cs        # (Ваня) Управление пользователями и ролями
│   │   ├── CourseController.cs       # (Ваня) Создание, просмотр и редактирование курсов
│   │   └── NotificationController.cs # (Вова) Отправка и просмотр уведомлений
│   │
│   ├── Data/
│   │   └── AppDbContext.cs           # Контекст БД Entity Framework Core
│   │
│   ├── DTOs/
│   │   ├── Auth/                     # RegisterDto, LoginDto, AuthResponseDto
│   │   └── User/                     # UserDto
│   │
│   ├── Extensions/
│   │   └── ClaimsPrincipalExtensions.cs # Extension-методы GetUserId(), GetUserRole()
│   │
│   ├── Middleware/
│   │   ├── AuthorizeRoleAttribute.cs # Кастомный атрибут разграничения ролей [AuthorizeRole]
│   │   └── ErrorHandlingMiddleware.cs # Глобальная обработка ошибок (JSON 401, 403, 500)
│   │
│   ├── Models/
│   │   ├── User.cs                   # Модель пользователя (Id, Email, PasswordHash, FullName, Role, CreatedAt)
│   │   ├── UserRole.cs               # Enum (Admin, Teacher, Student)
│   │   ├── Course.cs                 # Модель курса (теория, видео, презентации, задания)
│   │   ├── Enrollment.cs             # Запись на курс (процент прохождения)
│   │   └── Notification.cs           # Уведомления (дедлайны, добавления в БД)
│   │
│   ├── Services/
│   │   ├── IAuthService.cs
│   │   └── AuthService.cs            # Хэширование BCrypt, валидация, генерация JWT
│   │
│   ├── appsettings.json              # Конфигурация JWT, подключения к БД
│   ├── LmsPlatform.csproj            # Файл проекта .NET 8
│   └── Program.cs                    # Точка входа, DI, Swagger, JwtBearer, Policies
│
├── Dockerfile                        # Dockerfile сборки контейнера
├── .env                              # Переменные окружения
└── README.md                         # Документация
```

---

## 🔐 Аутентификация и авторизация (JWT)

### Эндпоинты аутентификации (`/api/auth`)
1. **`POST /api/auth/register`** — Регистрация нового пользователя
   - Request Body:
     ```json
     {
       "fullName": "Иван Иванов",
       "email": "user@example.com",
       "password": "secretpassword",
       "role": 2
     }
     ```
     *(Role: `0 = Admin`, `1 = Teacher`, `2 = Student`)*
   - Автоматически хэширует пароль с помощью BCrypt, проверяет уникальность e-mail и создает запись пользователя + приветственное уведомление в БД.

2. **`POST /api/auth/login`** — Вход в систему
   - Request Body:
     ```json
     {
       "email": "user@example.com",
       "password": "secretpassword"
     }
     ```
   - Response Body:
     ```json
     {
       "token": "eyJhbGciOiJIUzI1Ni...",
       "userId": "guid",
       "email": "user@example.com",
       "fullName": "Иван Иванов",
       "role": "Student",
       "expiresAt": "2026-11-16T20:49:00Z"
     }
     ```

3. **`GET /api/auth/me`** — Профиль текущего пользователя (требует `Authorization: Bearer {token}`).

---

## 🛡 Разграничение прав доступа (Middleware & Attributes)

В контроллерах используется кастомный атрибут `[AuthorizeRole]`:
```csharp
[AuthorizeRole(UserRole.Admin, UserRole.Teacher)]
public class CourseController : ControllerBase { ... }
```

### Ответы при ошибках доступа:
- **`401 Unauthorized`** (токен отсутствует или невалиден):
  ```json
  {
    "statusCode": 401,
    "message": "Unauthorized: Authentication token is missing or invalid."
  }
  ```
- **`403 Forbidden`** (недостаточно прав ролей):
  ```json
  {
    "statusCode": 403,
    "message": "Forbidden: You do not have permission to access this resource."
  }
  ```

---

## 🚀 Запуск проекта

### Локальный запуск (с помощью .NET SDK):
```bash
cd lms-platform/src
dotnet run
```
Swagger UI доступен по адресу: `http://localhost:5000/swagger` или `http://localhost:5242/swagger`

### Запуск в Docker:
```bash
docker build -t lms-platform .
docker run -d -p 8080:8080 --name lms-app lms-platform
```
Swagger UI доступен по адресу: `http://localhost:8080/swagger`
