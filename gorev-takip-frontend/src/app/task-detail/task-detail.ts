import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import {
  TaskService, TaskItem, ChecklistItem, TaskAttachment, TaskActivityItem
} from '../services/task';
import { ChecklistService } from '../services/checklist';
import { AttachmentService, formatFileSize, saveBlobAs } from '../services/attachment';
import { CommentService, TaskCommentItem } from '../services/comment';
import { AuthService } from '../services/auth';
import { UserDirectoryService, UserSummary, resolveAvatarUrl } from '../services/user-directory';
import { ProjectService, Project } from '../services/project';

/**
 * Görevin kendi adresi olan sayfası. Önceden görev yalnızca panodan açılan bir pencerede
 * yaşıyordu; kendi adresi olmadığı için bir görevi arkadaşına gönderemiyor, bildirime
 * tıklayınca oraya gidemiyor, sekmede açık tutamıyordun.
 */
@Component({
  selector: 'app-task-detail',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './task-detail.html',
  styleUrl: './task-detail.css'
})
export class TaskDetail implements OnInit, OnDestroy {
  taskId = 0;
  task: TaskItem | null = null;

  checklist: ChecklistItem[] = [];
  attachments: TaskAttachment[] = [];

  /**
   * Görsel eklerin önizlemesi için üretilen geçici tarayıcı adresleri (ek kimliği -> adres).
   * Ekler yetkili uçtan geldiği için doğrudan <img src="...api..."> kullanamıyoruz:
   * tarayıcının kendi başına attığı istek Authorization başlığını taşımaz ve 401 alır.
   */
  private previewUrls = new Map<number, string>();

  /** Aynı ek için üst üste indirme isteği gitmesin diye tıklanan ekin kimliği. */
  downloadingId: number | null = null;
  comments: TaskCommentItem[] = [];
  activities: TaskActivityItem[] = [];

  projects: Project[] = [];
  members: UserSummary[] = [];

  loading = true;
  notFound = false;
  errorMessage = '';
  savingField = '';

  newChecklistText = '';
  newComment = '';
  postingComment = false;
  uploading = false;
  deletingCommentId: number | null = null;

  /** Sağ sütunda hangi sekme açık: geçmiş mi yorumlar mı. */
  activeTab: 'comments' | 'activity' = 'comments';

  readonly statuses = [
    { key: 'todo', label: 'Yapılacak' },
    { key: 'in_progress', label: 'Devam Ediyor' },
    { key: 'done', label: 'Tamamlandı' }
  ];

  readonly priorities = [
    { key: 'Critical', label: 'Kritik' },
    { key: 'High', label: 'Yüksek' },
    { key: 'Medium', label: 'Orta' },
    { key: 'Low', label: 'Düşük' }
  ];

  private static readonly PRIORITY_COLORS: Record<string, { bg: string; fg: string; dot: string }> = {
    Critical: { bg: '#fee2e2', fg: '#b91c1c', dot: '#dc2626' },
    High: { bg: '#fff1e7', fg: '#c2410c', dot: '#ea580c' },
    Medium: { bg: '#eff6ff', fg: '#1d4ed8', dot: '#2563eb' },
    Low: { bg: '#f1f5f9', fg: '#475569', dot: '#64748b' }
  };

  private static readonly AVATAR_COLORS = ['#6366f1', '#0ea5e9', '#f59e0b', '#10b981', '#ec4899', '#8b5cf6'];

  // Geçmiş satırlarında hangi ikonun görüneceğini belirliyor.
  readonly activityIcons: Record<string, string> = {
    created: '✚', status: '⇄', assigned: '👤', priority: '▲', duedate: '📅',
    title: '✎', description: '✎', project: '📁', comment: '💬',
    attachment: '📎', checklist: '☑'
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private taskService: TaskService,
    private checklistService: ChecklistService,
    private attachmentService: AttachmentService,
    private commentService: CommentService,
    private projectService: ProjectService,
    private userDirectory: UserDirectoryService,
    public authService: AuthService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    // Aynı bileşen farklı bir görev için yeniden kullanıldığında (bildirimden bildirime
    // geçiş gibi) ngOnInit tekrar çalışmaz; bu yüzden parametreye abone oluyoruz.
    this.route.paramMap.subscribe(params => {
      const id = Number(params.get('id'));
      if (!id || Number.isNaN(id)) {
        this.notFound = true;
        this.loading = false;
        this.cdr.markForCheck();
        return;
      }
      this.taskId = id;
      this.load();
    });
  }

  private load(): void {
    this.loading = true;
    this.notFound = false;
    this.errorMessage = '';

    this.taskService.getTaskDetail(this.taskId).subscribe({
      next: (detail) => {
        this.task = detail.task;
        this.checklist = detail.checklist;
        this.attachments = detail.attachments;
        // Başka bir göreve geçilmiş olabilir; eski görsellerin geçici adreslerini bırakıyoruz.
        this.releasePreviews();
        this.loadImagePreviews();
        this.loading = false;
        this.cdr.markForCheck();
        this.loadComments();
        this.loadActivity();
      },
      error: (err) => {
        this.loading = false;
        this.notFound = err?.status === 404;
        if (!this.notFound) this.errorMessage = 'Görev yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });

    forkJoin({
      projects: this.projectService.getProjects(),
      users: this.userDirectory.getUsers()
    }).subscribe({
      next: ({ projects, users }) => {
        this.projects = projects;
        this.members = users;
        this.cdr.markForCheck();
      },
      error: () => { /* listeler gelmezse alanlar salt okunur kalır, sayfa yine çalışır */ }
    });
  }

  private loadComments(): void {
    this.commentService.getComments(this.taskId).subscribe({
      next: (comments) => { this.comments = comments; this.cdr.markForCheck(); },
      error: () => { this.comments = []; this.cdr.markForCheck(); }
    });
  }

  private loadActivity(): void {
    this.taskService.getActivity(this.taskId).subscribe({
      next: (activities) => { this.activities = activities; this.cdr.markForCheck(); },
      error: () => { this.activities = []; this.cdr.markForCheck(); }
    });
  }

  // ---------- Yetki ----------

  get canManage(): boolean {
    const role = this.authService.getRole();
    return role === 'Admin' || role === 'TeamLeader';
  }

  get isAssignedToMe(): boolean {
    return !!this.task && this.task.assignedToId === this.authService.getUserId();
  }

  /** Ekip üyesi kendi görevinin yalnızca durumunu değiştirebiliyor. */
  get canEditStatus(): boolean {
    return this.canManage || this.isAssignedToMe;
  }

  get canEditChecklist(): boolean {
    return this.canManage || this.isAssignedToMe;
  }

  // ---------- Alan güncelleme ----------

  /** Tam nesneyi PUT etmek için gereken gövdeyi hazırlar. */
  private payload(overrides: Partial<TaskItem> = {}) {
    const t = { ...this.task!, ...overrides };
    return {
      id: t.id,
      title: (t.title || '').trim(),
      description: (t.description || '').trim(),
      status: t.status,
      priority: t.priority,
      dueDate: t.dueDate ? t.dueDate : null,
      projectId: t.projectId,
      assignedToId: t.assignedToId ?? null
    };
  }

  changeStatus(status: string): void {
    if (!this.task || !this.canEditStatus || this.task.status === status) return;

    const previous = this.task.status;
    this.task.status = status;
    this.savingField = 'status';
    this.cdr.markForCheck();

    // Ekip üyesi tam nesne gönderemiyor (yetkisi yok); dar kapsamlı uç kullanılıyor.
    const request$ = this.canManage
      ? this.taskService.updateTask(this.task.id, this.payload({ status }))
      : this.taskService.updateTaskStatus(this.task.id, status);

    request$.subscribe({
      next: () => { this.savingField = ''; this.afterChange(); },
      error: () => this.revert('status', previous, 'Durum güncellenemedi.')
    });
  }

  updateField(field: 'title' | 'description' | 'priority' | 'dueDate' | 'projectId' | 'assignedToId', value: any): void {
    if (!this.task || !this.canManage) return;

    const previous = (this.task as any)[field];
    if (previous === value) return;

    (this.task as any)[field] = value;
    this.savingField = field;
    this.cdr.markForCheck();

    this.taskService.updateTask(this.task.id, this.payload()).subscribe({
      next: () => { this.savingField = ''; this.afterChange(); },
      error: () => this.revert(field, previous, 'Değişiklik kaydedilemedi.')
    });
  }

  private revert(field: string, previous: any, message: string): void {
    if (this.task) (this.task as any)[field] = previous;
    this.savingField = '';
    this.errorMessage = message;
    this.cdr.markForCheck();
  }

  /** Değişiklik sonrası geçmiş yeniden çekiliyor ki kayıt anında görünsün. */
  private afterChange(): void {
    this.errorMessage = '';
    this.loadActivity();
    this.cdr.markForCheck();
  }

  onTitleBlur(value: string): void {
    const trimmed = value.trim();
    if (!trimmed) { this.errorMessage = 'Başlık boş olamaz.'; this.cdr.markForCheck(); return; }
    this.updateField('title', trimmed);
  }

  // ---------- Kontrol listesi ----------

  get checklistDone(): number { return this.checklist.filter(c => c.isDone).length; }

  get checklistPercent(): number {
    return this.checklist.length === 0 ? 0 : Math.round((this.checklistDone / this.checklist.length) * 100);
  }

  addChecklistItem(): void {
    const text = this.newChecklistText.trim();
    if (!text || !this.canEditChecklist) return;

    this.checklistService.addItem(this.taskId, text).subscribe({
      next: (item) => {
        this.checklist.push(item);
        this.newChecklistText = '';
        this.loadActivity();
        this.cdr.markForCheck();
      },
      error: () => { this.errorMessage = 'Adım eklenemedi.'; this.cdr.markForCheck(); }
    });
  }

  toggleChecklistItem(item: ChecklistItem): void {
    if (!this.canEditStatus) return;

    const previous = item.isDone;
    item.isDone = !item.isDone;
    this.cdr.markForCheck();

    this.checklistService.updateItem(this.taskId, item.id, { isDone: item.isDone }).subscribe({
      next: () => this.loadActivity(),
      error: () => {
        item.isDone = previous;
        this.errorMessage = 'Adım güncellenemedi.';
        this.cdr.markForCheck();
      }
    });
  }

  deleteChecklistItem(item: ChecklistItem): void {
    if (!this.canEditChecklist) return;

    this.checklistService.deleteItem(this.taskId, item.id).subscribe({
      next: () => {
        this.checklist = this.checklist.filter(c => c.id !== item.id);
        this.loadActivity();
        this.cdr.markForCheck();
      },
      error: () => { this.errorMessage = 'Adım silinemedi.'; this.cdr.markForCheck(); }
    });
  }

  // ---------- Dosya ekleri ----------

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploading = true;
    this.errorMessage = '';
    this.cdr.markForCheck();

    this.attachmentService.upload(this.taskId, file).subscribe({
      next: (attachment) => {
        this.attachments.unshift(attachment);
        this.loadImagePreviews();
        this.uploading = false;
        input.value = '';
        this.loadActivity();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.uploading = false;
        input.value = '';
        this.errorMessage = err?.error?.message || err?.error || 'Dosya yüklenemedi.';
        this.cdr.markForCheck();
      }
    });
  }

  canDeleteAttachment(a: TaskAttachment): boolean {
    return this.canManage || a.userId === this.authService.getUserId();
  }

  deleteAttachment(a: TaskAttachment): void {
    if (!confirm(`"${a.fileName}" dosyasını silmek istediğinize emin misiniz?`)) return;

    this.attachmentService.delete(this.taskId, a.id).subscribe({
      next: () => {
        this.attachments = this.attachments.filter(x => x.id !== a.id);
        this.releasePreviews(new Set(this.attachments.map(x => x.id)));
        this.loadActivity();
        this.cdr.markForCheck();
      },
      error: () => { this.errorMessage = 'Dosya silinemedi.'; this.cdr.markForCheck(); }
    });
  }

  fileSize(bytes: number): string { return formatFileSize(bytes); }

  isImage(a: TaskAttachment): boolean { return a.contentType.startsWith('image/'); }

  /** Şablonun <img [src]> için okuduğu adres; henüz yüklenmediyse boş döner. */
  previewUrl(a: TaskAttachment): string | null {
    return this.previewUrls.get(a.id) ?? null;
  }

  /**
   * Görsel eklerin önizlemesini indirir. Liste her yenilendiğinde çağrılıyor; daha önce
   * indirilmiş olanlar tekrar istenmiyor.
   */
  private loadImagePreviews(): void {
    for (const attachment of this.attachments) {
      if (!this.isImage(attachment) || this.previewUrls.has(attachment.id)) continue;

      this.attachmentService.download(this.taskId, attachment.id).subscribe({
        next: (blob) => {
          this.previewUrls.set(attachment.id, URL.createObjectURL(blob));
          this.cdr.markForCheck();
        },
        error: () => {
          // Önizleme gelmezse satır dosya ikonuyla görünmeye devam eder; hata göstermiyoruz.
        }
      });
    }
  }

  /** Önizlemesi artık gerekmeyen adresleri serbest bırakır (bellek sızıntısını önler). */
  private releasePreviews(keepIds?: Set<number>): void {
    for (const [id, url] of this.previewUrls) {
      if (keepIds?.has(id)) continue;
      URL.revokeObjectURL(url);
      this.previewUrls.delete(id);
    }
  }

  ngOnDestroy(): void {
    this.releasePreviews();
  }

  /** Dosyayı indirir. Bağlantı yerine buton: yetki başlığı ancak böyle gönderilebiliyor. */
  downloadAttachment(a: TaskAttachment): void {
    if (this.downloadingId === a.id) return;

    this.downloadingId = a.id;
    this.cdr.markForCheck();

    this.attachmentService.download(this.taskId, a.id).subscribe({
      next: (blob) => {
        saveBlobAs(blob, a.fileName);
        this.downloadingId = null;
        this.cdr.markForCheck();
      },
      error: () => {
        this.downloadingId = null;
        this.errorMessage = 'Dosya indirilemedi.';
        this.cdr.markForCheck();
      }
    });
  }

  // ---------- Yorumlar ----------

  addComment(): void {
    const content = this.newComment.trim();
    if (!content) return;

    this.postingComment = true;
    this.commentService.addComment(this.taskId, content).subscribe({
      next: (comment) => {
        this.comments.push(comment);
        this.newComment = '';
        this.postingComment = false;
        this.loadActivity();
        this.cdr.markForCheck();
      },
      error: () => {
        this.postingComment = false;
        this.errorMessage = 'Yorum eklenemedi.';
        this.cdr.markForCheck();
      }
    });
  }

  canDeleteComment(c: TaskCommentItem): boolean {
    return c.userId === this.authService.getUserId() || this.authService.isAdmin();
  }

  deleteComment(c: TaskCommentItem): void {
    this.deletingCommentId = c.id;
    this.commentService.deleteComment(this.taskId, c.id).subscribe({
      next: () => {
        this.comments = this.comments.filter(x => x.id !== c.id);
        this.deletingCommentId = null;
        this.cdr.markForCheck();
      },
      error: () => {
        this.deletingCommentId = null;
        this.errorMessage = 'Yorum silinemedi.';
        this.cdr.markForCheck();
      }
    });
  }

  // ---------- Silme ----------

  deleteTask(): void {
    if (!this.task || !this.canManage) return;
    if (!confirm(`"${this.task.title}" görevini silmek istediğinize emin misiniz?`)) return;

    this.taskService.deleteTask(this.task.id).subscribe({
      next: () => this.router.navigate(['/board']),
      error: () => { this.errorMessage = 'Görev silinemedi.'; this.cdr.markForCheck(); }
    });
  }

  // ---------- Görünüm yardımcıları ----------

  priorityStyle(priority: string) {
    return TaskDetail.PRIORITY_COLORS[priority] ?? TaskDetail.PRIORITY_COLORS['Low'];
  }

  priorityLabel(key: string): string {
    return this.priorities.find(p => p.key === key)?.label ?? key;
  }

  statusLabel(key: string): string {
    return this.statuses.find(s => s.key === key)?.label ?? key;
  }

  avatarColor(userId: number | null | undefined): string {
    if (userId === null || userId === undefined) return '#94a3b8';
    return TaskDetail.AVATAR_COLORS[Math.abs(userId) % TaskDetail.AVATAR_COLORS.length];
  }

  initials(fullName: string | null | undefined): string {
    if (!fullName) return '?';
    return fullName.split(' ').filter(Boolean).slice(0, 2).map(p => p[0]?.toUpperCase()).join('');
  }

  avatarUrl(url: string | null | undefined): string | null {
    return resolveAvatarUrl(url);
  }

  memberAvatar(userId: number | null | undefined): string | null {
    if (userId === null || userId === undefined) return null;
    return resolveAvatarUrl(this.members.find(m => m.id === userId)?.avatarUrl);
  }

  /** <input type="date"> için yyyy-MM-dd biçimi gerekiyor. */
  get dueDateInput(): string {
    return this.task?.dueDate ? this.task.dueDate.substring(0, 10) : '';
  }

  onDueDateChange(value: string): void {
    // Saat dilimsiz metni UTC gece yarısına sabitliyoruz; aksi halde sunucudaki
    // "timestamp with time zone" kolonu bu değeri kabul etmiyor.
    const iso = value ? new Date(`${value}T00:00:00Z`).toISOString() : null;
    this.updateField('dueDate', iso);
  }

  activityIcon(type: string): string {
    return this.activityIcons[type] ?? '•';
  }

  /** Geçmiş ve yorumlarda "3 saat önce" gibi göreli zaman daha hızlı okunuyor. */
  relativeTime(value: string): string {
    const then = new Date(value).getTime();
    const diffMinutes = Math.round((Date.now() - then) / 60000);

    if (diffMinutes < 1) return 'az önce';
    if (diffMinutes < 60) return `${diffMinutes} dakika önce`;

    const hours = Math.floor(diffMinutes / 60);
    if (hours < 24) return `${hours} saat önce`;

    const days = Math.floor(hours / 24);
    if (days < 7) return `${days} gün önce`;

    return formatDate(value, 'd MMM y', 'tr');
  }

  copyLink(): void {
    const url = window.location.href;
    navigator.clipboard?.writeText(url).then(
      () => { this.errorMessage = ''; this.linkCopied = true; this.cdr.markForCheck();
              setTimeout(() => { this.linkCopied = false; this.cdr.markForCheck(); }, 2000); },
      () => { this.errorMessage = 'Bağlantı kopyalanamadı.'; this.cdr.markForCheck(); }
    );
  }

  linkCopied = false;
}
