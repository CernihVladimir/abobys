const API = '/api';

let token = localStorage.getItem('token');
let currentUser = JSON.parse(localStorage.getItem('user') || 'null');

function esc(value) {
    return String(value ?? '').replace(/[&<>"']/g, ch => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    }[ch]));
}

function qs(name) {
    return new URLSearchParams(location.search).get(name);
}

function fmtDate(value) {
    if (!value) return '—';
    return new Date(value).toLocaleString('ru-RU');
}

function roleName(role) {
    return { Admin: 'Администратор', Teacher: 'Преподаватель', Student: 'Студент' }[role] || role;
}

function statusName(status) {
    return {
        Draft: 'Черновик',
        PendingReview: 'На проверке',
        Published: 'Опубликован',
        Rejected: 'Отклонён'
    }[status] || status;
}

async function api(path, options = {}) {
    const headers = {};
    if (options.body) headers['Content-Type'] = 'application/json';
    if (token) headers.Authorization = 'Bearer ' + token;

    const response = await fetch(API + path, { ...options, headers });
    if (response.status === 204) return null;

    const data = await response.json().catch(() => null);
    if (!response.ok) {
        throw new Error((data && data.message) || 'Ошибка ' + response.status);
    }
    return data;
}

function saveSession(data) {
    token = data.token;
    currentUser = {
        id: data.userId,
        email: data.email,
        fullName: data.fullName,
        role: data.role
    };
    localStorage.setItem('token', token);
    localStorage.setItem('user', JSON.stringify(currentUser));
}

function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    location.href = '/index.html';
}

function renderHeader() {
    const nav = document.getElementById('nav');
    if (!nav) return;

    let html = '<a href="/index.html">Главная</a><a href="/courses.html">Курсы</a>';
    if (currentUser && currentUser.role === 'Admin') {
        html += '<a href="/admin.html">Админка</a>';
    }

    if (currentUser) {
        html += '<span>' + esc(currentUser.fullName) + ' (' + roleName(currentUser.role) + ')</span> ' +
            '<a href="#" id="logoutLink">Выйти</a>';
    } else {
        html += '<a href="/login.html">Войти</a>';
    }

    nav.innerHTML = html;

    const logoutLink = document.getElementById('logoutLink');
    if (logoutLink) {
        logoutLink.onclick = event => {
            event.preventDefault();
            logout();
        };
    }

    const box = document.getElementById('notifications');
    if (box) {
        box.hidden = !currentUser;
        if (currentUser) loadNotifications();
    }
}

async function loadNotifications() {
    const list = document.getElementById('notificationList');
    const badge = document.getElementById('unread');
    if (!list) return;

    try {
        const count = await api('/notifications/unread-count');
        if (badge) badge.textContent = count.count;

        const items = await api('/notifications');
        if (!items.length) {
            list.innerHTML = '<p class="muted">Уведомлений нет.</p>';
            return;
        }

        list.innerHTML = '<ul class="plain">' + items.map(n =>
            '<li class="' + (n.isRead ? '' : 'unread') + '">' +
                '<div>' + esc(n.message) + '</div>' +
                '<div class="muted">' + fmtDate(n.createdAt) +
                    (n.link ? ' · <a href="' + esc(n.link) + '">Открыть</a>' : '') +
                '</div>' +
                (n.isRead ? '' : '<button class="secondary" data-read="' + n.id + '">Прочитано</button>') +
            '</li>').join('') + '</ul>';

        list.querySelectorAll('[data-read]').forEach(button => {
            button.onclick = async () => {
                await api('/notifications/' + button.dataset.read + '/read', { method: 'PUT' });
                loadNotifications();
            };
        });
    } catch (error) {
        list.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

function requireLogin() {
    if (!currentUser) {
        location.href = '/login.html';
        return false;
    }
    return true;
}

function showMessage(id, text, isError) {
    const box = document.getElementById(id);
    if (!box) return;
    box.textContent = text;
    box.className = isError ? 'error' : 'success';
}

async function initLogin() {
    if (currentUser) {
        location.href = '/courses.html';
        return;
    }

    document.getElementById('loginForm').onsubmit = async event => {
        event.preventDefault();
        const form = event.target;
        try {
            const data = await api('/auth/login', {
                method: 'POST',
                body: JSON.stringify({
                    email: form.email.value,
                    password: form.password.value
                })
            });
            saveSession(data);
            location.href = data.role === 'Admin' ? '/admin.html' : '/courses.html';
        } catch (error) {
            showMessage('loginMessage', error.message, true);
        }
    };

    document.getElementById('registerForm').onsubmit = async event => {
        event.preventDefault();
        const form = event.target;
        try {
            const data = await api('/auth/register', {
                method: 'POST',
                body: JSON.stringify({
                    fullName: form.fullName.value,
                    email: form.email.value,
                    password: form.password.value,
                    role: form.role.value
                })
            });
            saveSession(data);
            location.href = '/courses.html';
        } catch (error) {
            showMessage('registerMessage', error.message, true);
        }
    };
}

async function initCourses() {
    if (!requireLogin()) return;

    const form = document.getElementById('filterForm');
    form.onsubmit = event => {
        event.preventDefault();
        loadCourses();
    };
    form.search.oninput = () => loadCourses();
    form.mine.onchange = () => loadCourses();
    form.status.onchange = () => loadCourses();

    if (currentUser.role === 'Student') {
        form.status.classList.add('hidden');
        form.status.parentElement.classList.add('hidden');
    }

    const createBox = document.getElementById('createCourseBox');
    if (currentUser.role === 'Teacher' || currentUser.role === 'Admin') {
        createBox.classList.remove('hidden');
        document.getElementById('createCourseForm').onsubmit = async event => {
            event.preventDefault();
            const data = new FormData(event.target);
            try {
                const course = await api('/courses', {
                    method: 'POST',
                    body: JSON.stringify({
                        title: data.get('title'),
                        description: data.get('description'),
                        theoryContent: data.get('theoryContent'),
                        videoUrls: lines(data.get('videoUrls')),
                        presentationUrls: lines(data.get('presentationUrls'))
                    })
                });
                location.href = '/course.html?id=' + course.id;
            } catch (error) {
                showMessage('createCourseMessage', error.message, true);
            }
        };
    }

    await loadCourses();

    if (currentUser.role === 'Admin') {
        loadModeration();
    }
}

function lines(value) {
    return String(value || '')
        .split('\n')
        .map(item => item.trim())
        .filter(item => item.length > 0);
}

async function loadCourses() {
    const form = document.getElementById('filterForm');
    const params = new URLSearchParams();
    if (form.search.value.trim()) params.set('search', form.search.value.trim());
    if (form.mine.checked) params.set('mine', 'true');
    if (form.status.value && currentUser.role !== 'Student') params.set('status', form.status.value);

    const list = document.getElementById('courseList');
    list.innerHTML = '<p class="muted">Загрузка...</p>';

    try {
        const courses = await api('/courses?' + params.toString());
        if (!courses.length) {
            list.innerHTML = '<p class="muted">Курсы не найдены.</p>';
            return;
        }

        list.innerHTML = courses.map(course =>
            '<div class="card">' +
                '<h3><a href="/course.html?id=' + course.id + '">' + esc(course.title) + '</a></h3>' +
                '<p>' + esc(course.description) + '</p>' +
                '<p class="muted">Преподаватель: ' + esc(course.teacherName) + '</p>' +
                '<p><span class="badge">' + statusName(course.status) + '</span> ' +
                    'Заданий: ' + course.assignmentsCount + ' · Записано: ' + course.enrolledCount +
                    (course.progressPercentage !== null ? ' · Мой прогресс: ' + course.progressPercentage + '%' : '') +
                '</p>' +
            '</div>').join('');
    } catch (error) {
        list.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function loadModeration() {
    const box = document.getElementById('moderation');
    box.classList.remove('hidden');

    try {
        const courses = await api('/courses?status=PendingReview');
        if (!courses.length) {
            box.innerHTML = '<h2>Курсы на проверке</h2><p class="muted">Нет курсов, ожидающих проверки.</p>';
            return;
        }

        box.innerHTML = '<h2>Курсы на проверке</h2><div class="grid">' + courses.map(course =>
            '<div class="card">' +
                '<h3><a href="/course.html?id=' + course.id + '">' + esc(course.title) + '</a></h3>' +
                '<p class="muted">' + esc(course.teacherName) + '</p>' +
                '<button data-approve="' + course.id + '">Опубликовать</button>' +
                '<button class="danger" data-reject="' + course.id + '">Отклонить</button>' +
            '</div>').join('') + '</div>';

        box.querySelectorAll('[data-approve]').forEach(button => {
            button.onclick = async () => {
                await api('/courses/' + button.dataset.approve + '/approve', { method: 'POST' });
                loadModeration();
                loadCourses();
            };
        });

        box.querySelectorAll('[data-reject]').forEach(button => {
            button.onclick = async () => {
                const comment = prompt('Причина отклонения:');
                if (comment === null) return;
                await api('/courses/' + button.dataset.reject + '/reject', {
                    method: 'POST',
                    body: JSON.stringify({ comment })
                });
                loadModeration();
                loadCourses();
            };
        });
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function initCourse() {
    if (!requireLogin()) return;

    const id = qs('id');
    const box = document.getElementById('course');
    if (!id) {
        box.innerHTML = '<p class="error">Курс не указан.</p>';
        return;
    }

    try {
        const course = await api('/courses/' + id);
        renderCourse(course);
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

function renderCourse(course) {
    const box = document.getElementById('course');
    const isAdmin = currentUser.role === 'Admin';
    const isTeacher = currentUser.role === 'Teacher' && course.teacherId === currentUser.id;
    const canManage = isAdmin || isTeacher;
    const isStudent = currentUser.role === 'Student';
    const enrolled = course.myProgress !== null && course.myProgress !== undefined;

    let html = '<section>' +
        '<h2>' + esc(course.title) + ' <span class="badge">' + statusName(course.status) + '</span></h2>' +
        '<p>' + esc(course.description) + '</p>' +
        '<p class="muted">Преподаватель: ' + esc(course.teacherName) +
            ' · Записано: ' + course.enrolledCount + '</p>';

    if (course.rejectionComment) {
        html += '<p class="error">Причина отклонения: ' + esc(course.rejectionComment) + '</p>';
    }

    if (isStudent) {
        if (enrolled) {
            html += '<p>Мой прогресс: <b>' + course.myProgress + '%</b></p>' +
                '<button class="secondary" id="unenrollButton">Отписаться от курса</button>';
        } else {
            html += '<button id="enrollButton">Записаться на курс</button>';
        }
    }

    if (canManage) {
        if (course.status !== 'Published' && (isTeacher || isAdmin)) {
            html += '<button id="submitReviewButton">Отправить на проверку</button>';
        }
        if (isAdmin && course.status === 'PendingReview') {
            html += '<button id="approveButton">Опубликовать</button>' +
                '<button class="danger" id="rejectButton">Отклонить</button>';
        }
        html += '<button class="danger" id="deleteCourseButton">Удалить курс</button>';
    }

    html += '</section>';

    if (course.theoryContent) {
        html += '<section><h2>Теория</h2><p style="white-space:pre-wrap">' + esc(course.theoryContent) + '</p></section>';
    }

    const materials = []
        .concat((course.videoUrls || []).map(url => ({ url, type: 'Видео' })))
        .concat((course.presentationUrls || []).map(url => ({ url, type: 'Презентация' })));

    if (materials.length) {
        html += '<section><h2>Материалы</h2><ul class="plain">' + materials.map(item =>
            '<li>' + item.type + ': <a href="' + esc(item.url) + '" target="_blank">' + esc(item.url) + '</a></li>'
        ).join('') + '</ul></section>';
    }

    html += '<section><h2>Задания</h2><div id="assignments"></div>';

    if (canManage) {
        html += '<h3>Добавить задание</h3>' +
            '<form id="assignmentForm">' +
                '<label>Название</label><input name="title" required>' +
                '<label>Описание</label><textarea name="description"></textarea>' +
                '<div class="row">' +
                    '<div><label>Дедлайн</label><input type="datetime-local" name="deadline"></div>' +
                    '<div><label>Баллы</label><input type="number" name="maxPoints" value="100" min="1" max="1000"></div>' +
                '</div>' +
                '<button type="submit">Добавить</button>' +
            '</form>';
    }

    html += '</section>';

    if (canManage) {
        html += '<section><h2>Студенты курса</h2><div id="students"><p class="muted">Загрузка...</p></div></section>' +
            '<section class="hidden" id="editBox"><h2>Редактировать курс</h2>' +
                '<form id="editCourseForm">' +
                    '<label>Название</label><input name="title" value="' + esc(course.title) + '" required>' +
                    '<label>Описание</label><textarea name="description">' + esc(course.description) + '</textarea>' +
                    '<label>Теория</label><textarea name="theoryContent">' + esc(course.theoryContent) + '</textarea>' +
                    '<label>Ссылки на видео (по одной в строке)</label><textarea name="videoUrls">' + esc((course.videoUrls || []).join('\n')) + '</textarea>' +
                    '<label>Ссылки на презентации (по одной в строке)</label><textarea name="presentationUrls">' + esc((course.presentationUrls || []).join('\n')) + '</textarea>' +
                    '<button type="submit">Сохранить</button>' +
                '</form>' +
            '</section>' +
            '<p><button class="secondary" id="editCourseButton">Редактировать курс</button></p>';
    }

    box.innerHTML = html;

    const enrollButton = document.getElementById('enrollButton');
    if (enrollButton) {
        enrollButton.onclick = async () => {
            await api('/courses/' + course.id + '/enroll', { method: 'POST' });
            location.reload();
        };
    }

    const unenrollButton = document.getElementById('unenrollButton');
    if (unenrollButton) {
        unenrollButton.onclick = async () => {
            if (!confirm('Отписаться от курса?')) return;
            await api('/courses/' + course.id + '/enroll', { method: 'DELETE' });
            location.reload();
        };
    }

    const deleteCourseButton = document.getElementById('deleteCourseButton');
    if (deleteCourseButton) {
        deleteCourseButton.onclick = async () => {
            if (!confirm('Удалить курс вместе с заданиями?')) return;
            await api('/courses/' + course.id, { method: 'DELETE' });
            location.href = '/courses.html';
        };
    }

    const submitReviewButton = document.getElementById('submitReviewButton');
    if (submitReviewButton) {
        submitReviewButton.onclick = async () => {
            await api('/courses/' + course.id + '/submit', { method: 'POST' });
            location.reload();
        };
    }

    const approveButton = document.getElementById('approveButton');
    if (approveButton) {
        approveButton.onclick = async () => {
            await api('/courses/' + course.id + '/approve', { method: 'POST' });
            location.reload();
        };
    }

    const rejectButton = document.getElementById('rejectButton');
    if (rejectButton) {
        rejectButton.onclick = async () => {
            const comment = prompt('Причина отклонения:');
            if (comment === null) return;
            await api('/courses/' + course.id + '/reject', {
                method: 'POST',
                body: JSON.stringify({ comment })
            });
            location.reload();
        };
    }

    const editCourseButton = document.getElementById('editCourseButton');
    if (editCourseButton) {
        editCourseButton.onclick = () => {
            document.getElementById('editBox').classList.toggle('hidden');
        };
    }

    const editCourseForm = document.getElementById('editCourseForm');
    if (editCourseForm) {
        editCourseForm.onsubmit = async event => {
            event.preventDefault();
            const data = new FormData(event.target);
            try {
                await api('/courses/' + course.id, {
                    method: 'PUT',
                    body: JSON.stringify({
                        title: data.get('title'),
                        description: data.get('description'),
                        theoryContent: data.get('theoryContent'),
                        videoUrls: lines(data.get('videoUrls')),
                        presentationUrls: lines(data.get('presentationUrls'))
                    })
                });
                location.reload();
            } catch (error) {
                alert(error.message);
            }
        };
    }

    const assignmentForm = document.getElementById('assignmentForm');
    if (assignmentForm) {
        assignmentForm.onsubmit = async event => {
            event.preventDefault();
            const data = new FormData(event.target);
            try {
                await api('/courses/' + course.id + '/assignments', {
                    method: 'POST',
                    body: JSON.stringify({
                        title: data.get('title'),
                        description: data.get('description'),
                        deadline: data.get('deadline') || null,
                        maxPoints: Number(data.get('maxPoints'))
                    })
                });
                location.reload();
            } catch (error) {
                alert(error.message);
            }
        };
    }

    renderAssignments(course, canManage);

    if (canManage) {
        loadStudents(course.id);
    }
}

function renderAssignments(course, canManage) {
    const box = document.getElementById('assignments');
    if (!course.assignments.length) {
        box.innerHTML = '<p class="muted">Заданий пока нет.</p>';
        return;
    }

    const enrolled = course.myProgress !== null && course.myProgress !== undefined;

    box.innerHTML = course.assignments.map(assignment => {
        const submission = assignment.mySubmission;
        let html = '<div class="card">' +
            '<h3>' + esc(assignment.title) + '</h3>' +
            '<p>' + esc(assignment.description) + '</p>' +
            '<p class="muted">Дедлайн: ' + fmtDate(assignment.deadline) +
                ' · Баллы: ' + assignment.maxPoints + '</p>';

        if (submission) {
            html += '<p>Мой ответ: ' + esc(submission.content) + '</p>' +
                '<p>Статус: <b>' + (submission.status === 'Solved' ? 'решено' : 'не решено') + '</b>' +
                (submission.comment ? ' · Комментарий: ' + esc(submission.comment) : '') + '</p>';
        }

        if (currentUser.role === 'Student' && enrolled) {
            html += '<textarea id="submission-' + assignment.id + '" placeholder="Ваше решение"></textarea>' +
                '<button data-submit="' + assignment.id + '">Отправить решение</button>';
        }

        if (currentUser.role === 'Student' && !enrolled) {
            html += '<p class="muted">Сначала запишитесь на курс.</p>';
        }

        if (canManage) {
            html += '<button class="secondary" data-edit="' + assignment.id + '">Изменить</button>' +
                '<button class="danger" data-remove="' + assignment.id + '">Удалить</button>' +
                '<button class="secondary" data-solutions="' + assignment.id + '">Решения</button>' +
                '<div id="solutions-' + assignment.id + '"></div>';
        }

        return html + '</div>';
    }).join('');

    box.querySelectorAll('[data-submit]').forEach(button => {
        button.onclick = async () => {
            const assignmentId = button.dataset.submit;
            const content = document.getElementById('submission-' + assignmentId).value;
            try {
                await api('/courses/' + course.id + '/assignments/' + assignmentId + '/submit', {
                    method: 'POST',
                    body: JSON.stringify({ content })
                });
                alert('Решение отправлено.');
                location.reload();
            } catch (error) {
                alert(error.message);
            }
        };
    });

    box.querySelectorAll('[data-remove]').forEach(button => {
        button.onclick = async () => {
            if (!confirm('Удалить задание?')) return;
            await api('/courses/' + course.id + '/assignments/' + button.dataset.remove, { method: 'DELETE' });
            location.reload();
        };
    });

    box.querySelectorAll('[data-edit]').forEach(button => {
        button.onclick = async () => {
            const assignment = course.assignments.find(item => item.id === button.dataset.edit);
            const title = prompt('Название задания:', assignment.title);
            if (title === null) return;
            const description = prompt('Описание:', assignment.description);
            if (description === null) return;
            const maxPoints = prompt('Баллы:', assignment.maxPoints);
            if (maxPoints === null) return;

            try {
                await api('/courses/' + course.id + '/assignments/' + assignment.id, {
                    method: 'PUT',
                    body: JSON.stringify({
                        title,
                        description,
                        maxPoints: Number(maxPoints)
                    })
                });
                location.reload();
            } catch (error) {
                alert(error.message);
            }
        };
    });

    box.querySelectorAll('[data-solutions]').forEach(button => {
        button.onclick = () => loadSolutions(course.id, button.dataset.solutions);
    });
}

async function loadSolutions(courseId, assignmentId) {
    const box = document.getElementById('solutions-' + assignmentId);
    box.innerHTML = '<p class="muted">Загрузка...</p>';

    try {
        const submissions = await api('/courses/' + courseId + '/assignments/' + assignmentId + '/submissions');
        if (!submissions.length) {
            box.innerHTML = '<p class="muted">Решений пока нет.</p>';
            return;
        }

        box.innerHTML = '<table><tr><th>Студент</th><th>Решение</th><th>Статус</th><th></th></tr>' +
            submissions.map(submission =>
                '<tr>' +
                    '<td>' + esc(submission.studentName) + '</td>' +
                    '<td>' + esc(submission.content) + '</td>' +
                    '<td>' + (submission.status === 'Solved' ? 'решено' : 'не решено') +
                        (submission.comment ? '<br><span class="muted">' + esc(submission.comment) + '</span>' : '') + '</td>' +
                    '<td><button data-review="' + submission.id + '" data-status="Solved">Решено</button>' +
                        '<button class="danger" data-review="' + submission.id + '" data-status="NotSolved">Не решено</button></td>' +
                '</tr>').join('') + '</table>';

        box.querySelectorAll('[data-review]').forEach(button => {
            button.onclick = async () => {
                const comment = prompt('Комментарий к проверке:');
                if (comment === null) return;
                await api('/courses/' + courseId + '/assignments/' + assignmentId + '/submissions/' + button.dataset.review, {
                    method: 'PUT',
                    body: JSON.stringify({ status: button.dataset.status, comment })
                });
                loadSolutions(courseId, assignmentId);
            };
        });
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function loadStudents(courseId) {
    const box = document.getElementById('students');
    try {
        const students = await api('/courses/' + courseId + '/students');
        if (!students.length) {
            box.innerHTML = '<p class="muted">Студентов пока нет.</p>';
            return;
        }

        box.innerHTML = '<table><tr><th>ФИО</th><th>Email</th><th>Прогресс</th></tr>' +
            students.map(student =>
                '<tr><td>' + esc(student.fullName) + '</td><td>' + esc(student.email) + '</td>' +
                '<td>' + student.progressPercentage + '%</td></tr>').join('') + '</table>';
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function initAdmin() {
    if (!requireLogin()) return;
    if (currentUser.role !== 'Admin') {
        location.href = '/courses.html';
        return;
    }

    document.getElementById('userForm').onsubmit = async event => {
        event.preventDefault();
        const data = new FormData(event.target);
        try {
            await api('/admin/users', {
                method: 'POST',
                body: JSON.stringify({
                    fullName: data.get('fullName'),
                    email: data.get('email'),
                    password: data.get('password'),
                    role: data.get('role')
                })
            });
            event.target.reset();
            loadUsers();
            loadStats();
        } catch (error) {
            alert(error.message);
        }
    };

    const filter = document.getElementById('userFilter');
    filter.onsubmit = event => {
        event.preventDefault();
        loadUsers();
    };
    filter.search.oninput = () => loadUsers();
    filter.role.onchange = () => loadUsers();

    await loadStats();
    await loadUsers();
}

async function loadStats() {
    const box = document.getElementById('stats');
    try {
        const stats = await api('/admin/stats');
        box.innerHTML = '<table>' +
            '<tr><th>Пользователи</th><td>' + stats.totalUsers + '</td></tr>' +
            '<tr><th>Студенты</th><td>' + stats.students + '</td></tr>' +
            '<tr><th>Преподаватели</th><td>' + stats.teachers + '</td></tr>' +
            '<tr><th>Администраторы</th><td>' + stats.admins + '</td></tr>' +
            '<tr><th>Курсы</th><td>' + stats.courses + ' (опубликовано: ' + stats.publishedCourses +
                ', на проверке: ' + stats.pendingCourses + ')</td></tr>' +
            '<tr><th>Зачисления</th><td>' + stats.enrollments + '</td></tr>' +
            '<tr><th>Решения</th><td>' + stats.submissions + '</td></tr>' +
            '</table>';
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function loadUsers() {
    const form = document.getElementById('userFilter');
    const params = new URLSearchParams();
    if (form.search.value.trim()) params.set('search', form.search.value.trim());
    if (form.role.value) params.set('role', form.role.value);

    const box = document.getElementById('users');
    box.innerHTML = '<p class="muted">Загрузка...</p>';

    try {
        const users = await api('/admin/users?' + params.toString());
        if (!users.length) {
            box.innerHTML = '<p class="muted">Пользователи не найдены.</p>';
            return;
        }

        box.innerHTML = '<table><tr><th>ФИО</th><th>Email</th><th>Роль</th><th>Создан</th><th>Действия</th></tr>' +
            users.map(user =>
                '<tr>' +
                    '<td>' + esc(user.fullName) + '</td>' +
                    '<td>' + esc(user.email) + '</td>' +
                    '<td><select data-role="' + user.id + '">' +
                        ['Admin', 'Teacher', 'Student'].map(role =>
                            '<option value="' + role + '"' + (role === user.role ? ' selected' : '') + '>' +
                            roleName(role) + '</option>').join('') +
                    '</select></td>' +
                    '<td>' + fmtDate(user.createdAt) + '</td>' +
                    '<td>' +
                        '<button class="secondary" data-edit="' + user.id + '">Изменить</button>' +
                        '<button class="secondary" data-enrollments="' + user.id + '">Зачисления</button>' +
                        '<button class="danger" data-delete="' + user.id + '">Удалить</button>' +
                    '</td>' +
                '</tr>').join('') + '</table>' +
            '<div id="enrollments"></div>';

        box.querySelectorAll('[data-role]').forEach(select => {
            select.onchange = async () => {
                try {
                    await api('/admin/users/' + select.dataset.role + '/role', {
                        method: 'PUT',
                        body: JSON.stringify({ role: select.value })
                    });
                    loadUsers();
                } catch (error) {
                    alert(error.message);
                }
            };
        });

        box.querySelectorAll('[data-edit]').forEach(button => {
            button.onclick = async () => {
                const user = users.find(item => item.id === button.dataset.edit);
                const fullName = prompt('ФИО:', user.fullName);
                if (fullName === null) return;
                const email = prompt('Email:', user.email);
                if (email === null) return;
                const password = prompt('Новый пароль (пусто — не менять):', '');
                if (password === null) return;

                try {
                    await api('/admin/users/' + user.id, {
                        method: 'PUT',
                        body: JSON.stringify({ fullName, email, password: password || null })
                    });
                    loadUsers();
                } catch (error) {
                    alert(error.message);
                }
            };
        });

        box.querySelectorAll('[data-delete]').forEach(button => {
            button.onclick = async () => {
                if (!confirm('Удалить пользователя?')) return;
                try {
                    await api('/admin/users/' + button.dataset.delete, { method: 'DELETE' });
                    loadUsers();
                    loadStats();
                } catch (error) {
                    alert(error.message);
                }
            };
        });

        box.querySelectorAll('[data-enrollments]').forEach(button => {
            button.onclick = () => loadEnrollments(button.dataset.enrollments, users);
        });
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

async function loadEnrollments(userId, users) {
    const box = document.getElementById('enrollments');
    const user = users.find(item => item.id === userId);
    box.innerHTML = '<h2>Зачисления: ' + esc(user.fullName) + '</h2><p class="muted">Загрузка...</p>';

    try {
        const [enrollments, courses] = await Promise.all([
            api('/admin/users/' + userId + '/enrollments'),
            api('/courses')
        ]);

        box.innerHTML = '<h2>Зачисления: ' + esc(user.fullName) + '</h2>' +
            (enrollments.length
                ? '<table><tr><th>Курс</th><th>Прогресс</th><th>Действия</th></tr>' +
                    enrollments.map(enrollment =>
                        '<tr><td>' + esc(enrollment.courseTitle) + '</td>' +
                        '<td>' + enrollment.progressPercentage + '%</td>' +
                        '<td><button class="secondary" data-progress="' + enrollment.courseId + '">Изменить прогресс</button>' +
                        '<button class="danger" data-unenroll="' + enrollment.courseId + '">Удалить</button></td></tr>').join('') +
                    '</table>'
                : '<p class="muted">Зачислений нет.</p>') +
            '<h3>Добавить зачисление</h3>' +
            '<form id="enrollmentForm"><div class="row">' +
                '<div><label>Курс</label><select name="courseId">' +
                    courses.map(course => '<option value="' + course.id + '">' + esc(course.title) + '</option>').join('') +
                '</select></div>' +
                '<div><label>Прогресс, %</label><input type="number" name="progress" value="0" min="0" max="100"></div>' +
                '<div><button type="submit">Добавить</button></div>' +
            '</div></form>';

        document.getElementById('enrollmentForm').onsubmit = async event => {
            event.preventDefault();
            const data = new FormData(event.target);
            try {
                await api('/admin/users/' + userId + '/enrollments', {
                    method: 'POST',
                    body: JSON.stringify({
                        courseId: data.get('courseId'),
                        progressPercentage: Number(data.get('progress'))
                    })
                });
                loadEnrollments(userId, users);
            } catch (error) {
                alert(error.message);
            }
        };

        box.querySelectorAll('[data-progress]').forEach(button => {
            button.onclick = async () => {
                const value = prompt('Прогресс, %:', '0');
                if (value === null) return;
                try {
                    await api('/admin/users/' + userId + '/enrollments/' + button.dataset.progress, {
                        method: 'PUT',
                        body: JSON.stringify({ progressPercentage: Number(value) })
                    });
                    loadEnrollments(userId, users);
                } catch (error) {
                    alert(error.message);
                }
            };
        });

        box.querySelectorAll('[data-unenroll]').forEach(button => {
            button.onclick = async () => {
                if (!confirm('Удалить зачисление?')) return;
                await api('/admin/users/' + userId + '/enrollments/' + button.dataset.unenroll, { method: 'DELETE' });
                loadEnrollments(userId, users);
            };
        });
    } catch (error) {
        box.innerHTML = '<p class="error">' + esc(error.message) + '</p>';
    }
}

function initIndex() {
    const box = document.getElementById('welcome');
    if (currentUser) {
        box.innerHTML = '<p>Вы вошли как <b>' + esc(currentUser.fullName) + '</b> (' +
            roleName(currentUser.role) + ').</p><p><a href="/courses.html">Перейти к курсам</a></p>';
    } else {
        box.innerHTML = '<p>Войдите или зарегистрируйтесь, чтобы начать работу.</p>' +
            '<p><a href="/login.html">Вход и регистрация</a></p>';
    }
}

renderHeader();

const page = document.body.dataset.page;

if (page === 'index') initIndex();
if (page === 'login') initLogin();
if (page === 'courses') initCourses();
if (page === 'course') initCourse();
if (page === 'admin') initAdmin();
