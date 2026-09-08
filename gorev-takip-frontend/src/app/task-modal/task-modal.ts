import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { TaskService, TaskItem } from '../services/task';
import { ProjectService, Project } from '../services/project';
import { UserDirectoryService, UserSummary, resolveAvatarUrl } from '../services/user-directory';
import { AuthService } from '../services/auth';
import { CommentService, TaskCommentItem } from '../services/comment';

@Component({
  selector: 'app-task-modal',
  imports: [CommonModule, FormsModule],
  templateUrl: './task-modal.html',
  styleUrl: './task-modal.css'
})
export class TaskModal implements OnInit {
  @Input() task: TaskItem | null = null;
  @Input() initialStatus = 'todo';
  @Output() closed = new EventEmitter<boolean>();

  projects: Project[] = [];
  users: UserSummary[] = [];
  loadingLists = true;

  title = '';
  description = '';
  status = 'todo';
  priority = 'Medium';
  dueDate = '';
  projectId: number | null = null;
  assignedToId: number | null = null;

  saving = false;
  deleting = false;
  errorMessage = '';

  comments: TaskCommentItem[] = [];
  loadingComments = false;
  newComment = '';
  postingComment = false;
  deletingCommentId: number | null = null;

  readonly priorityOptions: { value: string; label: string }[] = [
    { value: 'Low', label: 'Düşük' },
    { value: 'Medium', label: 'Orta' },
    { value: 'High', label: 'Yüksek' },
    { value: 'Critical', label: 'Kritik' }
  ];

  constructor(
    private taskService: TaskService,
    private projectService: ProjectService,
    private userDirectory: UserDirectoryService,
    private authService: AuthService,
    private commentService: CommentService,
    private cdr: ChangeDetectorRef
  ) { }

  get isEditMode(): boolean {
    return !!this.task;
  }

  /**
   * Admin ve Ekip Lideri "yönetici" sayılır: görev oluşturabilir/silebilir/başkasına
   * atayabilir. Sıradan Ekip Üyesi bunları yapamaz.
   */
  get canManage(): boolean {
    const role = this.authService.getRole();
    return role === 'Admin' || role === 'TeamLeader';
  }

  get isAssignedToMe(): boolean {
    return !!this.task && this.task.assignedToId === this.authService.getUserId();
  }

  /** Yönetici değil ama görev kendisine atanmış - sadece durumu değiştirebilir. */
  get canEditStatusOnly(): boolean {
    return !this.canManage && this.isAssignedToMe;
  }

  /** Ne yönetici ne de görev sahibi - sadece görüntüleyebilir. */
  get readOnly(): boolean {
    return this.isEditMode && !this.canManage && !this.isAssignedToMe;
  }

  get otherFieldsDisabled(): boolean {
    return this.saving || this.deleting || this.readOnly || this.canEditStatusOnly;
  }

  get statusDisabled(): boolean {
    return this.saving || this.deleting || this.readOnly;
  }

  get showSaveButton(): boolean {
    return this.canManage || this.canEditStatusOnly;
  }

  get showDeleteButton(): boolean {
    return this.isEditMode && this.canManage;
  }

  ngOnInit(): void {
    // Ekip üyesi "+" butonunu göremiyor olsa da (board bunu gizliyor), savunma amaçlı:
    // yeni görev oluşturma moduna yetkisiz biri bir şekilde ulaşırsa modal'ı kapat.
    if (!this.task && !this.canManage) {
      this.closed.emit(false);
      return;
    }

    if (this.task) {
      this.title = this.task.title;
      this.description = this.task.description;
      this.status = this.task.status;
      this.priority = this.task.priority || 'Medium';
      this.dueDate = this.task.dueDate ? this.task.dueDate.substring(0, 10) : '';
      this.projectId = this.task.projectId;
      this.assignedToId = this.task.assignedToId ?? null;
      this.loadComments();
    } else {
      this.status = this.initialStatus;
    }

    forkJoin({
      projects: this.projectService.getProjects(),
      users: this.userDirectory.getUsers()
    }).subscribe({
      next: ({ projects, users }) => {
        this.projects = projects;
        this.users = users;
        this.loadingLists = false;

        if (!this.isEditMode && this.projectId === null && projects.length > 0) {
          this.projectId = projects[0].id;
        }

        // Proje zoneless çalışıyor (zone.js yok); HTTP subscribe callback'i içindeki
        // değişiklikler otomatik yeniden render tetiklemiyor, elle bildiriyoruz.
        // Bu satır olmadan istek başarıyla dönse bile modal "Yükleniyor..." da takılı kalır.
        this.cdr.markForCheck();
      },
      error: () => {
        this.loadingLists = false;
        this.errorMessage = 'Proje/kullanıcı listesi yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  save(): void {
    if (!this.canEditStatusOnly) {
      if (!this.title.trim()) {
        this.errorMessage = 'Başlık zorunludur.';
        return;
      }
      if (this.projectId === null) {
        this.errorMessage = 'Bir proje seçmelisiniz.';
        return;
      }
    }

    this.errorMessage = '';
    this.saving = true;

    // Ekip üyesi yalnızca durumu değiştirebiliyor; tam nesne yerine dar kapsamlı uç
    // noktayı kullanıyoruz. Önceden tam nesne gönderiliyor, backend'deki alan-alan
    // eşitlik kontrolü trim/tarih biçimi farkları yüzünden tutmuyor ve 403 dönüyordu.
    if (this.canEditStatusOnly && this.isEditMode) {
      this.taskService.updateTaskStatus(this.task!.id, this.status).subscribe({
        next: () => {
          this.saving = false;
          this.closed.emit(true);
        },
        error: () => {
          this.saving = false;
          this.errorMessage = 'Kaydedilirken bir hata oluştu, tekrar deneyin.';
          this.cdr.markForCheck();
        }
      });
      return;
    }

    const payload = {
      id: this.task?.id ?? 0,
      title: this.title.trim(),
      description: this.description.trim(),
      status: this.status,
      priority: this.priority,
      // <input type="date"> "2026-08-27" gibi saat dilimsiz bir metin veriyor. Bu haliyle
      // gönderilince sunucuda Kind=Unspecified bir tarih oluşuyor ve PostgreSQL'deki
      // "timestamp with time zone" kolonuna yazılamadığı için bitiş tarihi verilen hiçbir
      // görev kaydedilemiyordu. Günü koruyarak UTC gece yarısına sabitliyoruz.
      dueDate: this.dueDate ? new Date(`${this.dueDate}T00:00:00Z`).toISOString() : null,
      projectId: this.projectId as number,
      assignedToId: this.assignedToId
    };

    // Tip birleşimi (Observable<void> | Observable<TaskItem>) TypeScript'in subscribe
    // çağrısını çözümlemesini engelliyordu; tek bir Observable<TaskItem | void> tipine sabitliyoruz.
    const request$: Observable<TaskItem | void> = this.isEditMode
      ? this.taskService.updateTask(this.task!.id, payload)
      : this.taskService.createTask(payload);

    request$.subscribe({
      next: () => {
        this.saving = false;
        this.closed.emit(true);
      },
      error: () => {
        this.saving = false;
        this.errorMessage = 'Kaydedilirken bir hata oluştu, tekrar deneyin.';
        this.cdr.markForCheck();
      }
    });
  }

  deleteTask(): void {
    if (!this.task) return;
    if (!confirm(`"${this.task.title}" görevini silmek istediğinize emin misiniz?`)) return;

    this.deleting = true;
    this.taskService.deleteTask(this.task.id).subscribe({
      next: () => {
        this.deleting = false;
        this.closed.emit(true);
      },
      error: () => {
        this.deleting = false;
        this.errorMessage = 'Silinirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  cancel(): void {
    this.closed.emit(false);
  }

  commentsFailed = false;

  loadComments(): void {
    if (!this.task) return;
    this.loadingComments = true;
    this.commentsFailed = false;
    this.commentService.getComments(this.task.id).subscribe({
      next: (comments) => {
        this.comments = comments;
        this.loadingComments = false;
        this.cdr.markForCheck();
      },
      // Yükleme başarısız olduğunda "Henüz yorum yok" gösteriliyordu; yani hata, boş
      // liste gibi görünüyor ve kullanıcı var olan yorumları kaçırıyordu.
      error: () => {
        this.loadingComments = false;
        this.commentsFailed = true;
        this.cdr.markForCheck();
      }
    });
  }

  addComment(): void {
    const content = this.newComment.trim();
    if (!content || !this.task) return;

    this.postingComment = true;
    this.commentService.addComment(this.task.id, content).subscribe({
      next: (comment) => {
        this.comments.push(comment);
        this.newComment = '';
        this.postingComment = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.postingComment = false;
        this.errorMessage = 'Yorum eklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  canDeleteComment(comment: TaskCommentItem): boolean {
    return comment.userId === this.authService.getUserId() || this.authService.isAdmin();
  }

  deleteComment(comment: TaskCommentItem): void {
    if (!this.task) return;
    this.deletingCommentId = comment.id;
    this.commentService.deleteComment(this.task.id, comment.id).subscribe({
      next: () => {
        this.comments = this.comments.filter(c => c.id !== comment.id);
        this.deletingCommentId = null;
        this.cdr.markForCheck();
      },
      error: () => {
        this.deletingCommentId = null;
        this.cdr.markForCheck();
      }
    });
  }

  commentAvatarUrl(comment: TaskCommentItem): string | null {
    return resolveAvatarUrl(comment.userAvatarUrl);
  }

  commentInitials(fullName: string): string {
    return fullName
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase())
      .join('');
  }
}
