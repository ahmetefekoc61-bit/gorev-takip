import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule, formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TaskService, TaskItem } from '../services/task';
import { AuthService } from '../services/auth';

interface WorkGroup {
  key: string;
  label: string;
  hint: string;
  tone: 'overdue' | 'today' | 'week' | 'later' | 'none';
  tasks: TaskItem[];
}

/**
 * "Bugün ne yapacağım?" ekranı.
 *
 * Pano bir planlama görünümü: tüm ekibin işlerini durum sütunlarına böler. Kişinin sabah
 * ihtiyaç duyduğu şey ise projelerden bağımsız, tarihe göre sıralanmış tek bir liste.
 * Bu yüzden burada gruplama duruma göre değil, aciliyete göre yapılıyor.
 */
@Component({
  selector: 'app-my-work',
  imports: [CommonModule, RouterLink],
  templateUrl: './my-work.html',
  styleUrl: './my-work.css'
})
export class MyWork implements OnInit {
  loading = true;
  errorMessage = '';

  myTasks: TaskItem[] = [];
  groups: WorkGroup[] = [];

  /** Tamamlananlar varsayılan olarak kapalı; bu ekran yapılacak işler için. */
  showCompleted = false;
  completedTasks: TaskItem[] = [];

  readonly priorityLabels: Record<string, string> = {
    Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük'
  };

  private static readonly PRIORITY_COLORS: Record<string, { bg: string; fg: string; dot: string }> = {
    Critical: { bg: '#fee2e2', fg: '#b91c1c', dot: '#dc2626' },
    High: { bg: '#fff1e7', fg: '#c2410c', dot: '#ea580c' },
    Medium: { bg: '#eff6ff', fg: '#1d4ed8', dot: '#2563eb' },
    Low: { bg: '#f1f5f9', fg: '#475569', dot: '#64748b' }
  };

  private static readonly PROJECT_COLORS = [
    { bg: '#eef2ff', fg: '#4338ca' }, { bg: '#fef3c7', fg: '#92400e' },
    { bg: '#dcfce7', fg: '#166534' }, { bg: '#fce7f3', fg: '#9d174d' },
    { bg: '#e0f2fe', fg: '#075985' }, { bg: '#f3e8ff', fg: '#6b21a8' }
  ];

  constructor(
    private taskService: TaskService,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';

    this.taskService.getTasks().subscribe({
      next: (tasks) => {
        const myId = this.authService.getUserId();
        const mine = tasks.filter(t => t.assignedToId === myId);

        this.myTasks = mine.filter(t => t.status !== 'done');
        this.completedTasks = mine.filter(t => t.status === 'done');
        this.groups = this.buildGroups(this.myTasks);

        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Görevler yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * Görevleri aciliyete göre böler. Tarihi olmayan işler en sona düşüyor - tarihi olan
   * işlerin arasına karışırsa gerçekten acil olanları gölgeliyorlar.
   */
  private buildGroups(tasks: TaskItem[]): WorkGroup[] {
    const buckets: Record<string, TaskItem[]> = { overdue: [], today: [], week: [], later: [], none: [] };

    for (const task of tasks) {
      const diff = this.daysUntilDue(task);
      if (diff === null) buckets['none'].push(task);
      else if (diff < 0) buckets['overdue'].push(task);
      else if (diff === 0) buckets['today'].push(task);
      else if (diff <= 7) buckets['week'].push(task);
      else buckets['later'].push(task);
    }

    // Grup içinde önce tarihe, tarih eşitse önceliğe göre sıralıyoruz.
    const priorityRank: Record<string, number> = { Critical: 0, High: 1, Medium: 2, Low: 3 };
    const sorter = (a: TaskItem, b: TaskItem) => {
      const da = a.dueDate ? new Date(a.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
      const db = b.dueDate ? new Date(b.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
      if (da !== db) return da - db;
      return (priorityRank[a.priority] ?? 9) - (priorityRank[b.priority] ?? 9);
    };

    const defs: { key: string; label: string; hint: string; tone: WorkGroup['tone'] }[] = [
      { key: 'overdue', label: 'Geciken', hint: 'Bitiş tarihi geçmiş', tone: 'overdue' },
      { key: 'today', label: 'Bugün', hint: 'Bugün bitmesi gereken', tone: 'today' },
      { key: 'week', label: 'Bu hafta', hint: 'Önümüzdeki 7 gün', tone: 'week' },
      { key: 'later', label: 'Sonra', hint: '7 günden uzak', tone: 'later' },
      { key: 'none', label: 'Tarihsiz', hint: 'Bitiş tarihi belirlenmemiş', tone: 'none' }
    ];

    return defs
      .map(d => ({ ...d, tasks: buckets[d.key].sort(sorter) }))
      .filter(g => g.tasks.length > 0);
  }

  private startOfDay(value: Date): Date {
    const d = new Date(value);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  private daysUntilDue(task: TaskItem): number | null {
    if (!task.dueDate) return null;
    const due = this.startOfDay(new Date(task.dueDate)).getTime();
    const today = this.startOfDay(new Date()).getTime();
    return Math.round((due - today) / 86400000);
  }

  get overdueCount(): number {
    return this.groups.find(g => g.key === 'overdue')?.tasks.length ?? 0;
  }

  get todayCount(): number {
    return this.groups.find(g => g.key === 'today')?.tasks.length ?? 0;
  }

  toggleCompleted(): void {
    this.showCompleted = !this.showCompleted;
    this.cdr.markForCheck();
  }

  // ---------- Görünüm ----------

  dueLabel(task: TaskItem): string {
    const diff = this.daysUntilDue(task);
    if (diff === null) return '';
    if (diff === 0) return 'Bugün';
    if (diff === 1) return 'Yarın';
    if (diff === -1) return 'Dün';
    if (diff < 0) return `${Math.abs(diff)} gün gecikti`;
    return formatDate(task.dueDate!, 'd MMM', 'tr');
  }

  statusLabel(status: string): string {
    if (status === 'in_progress') return 'Devam Ediyor';
    if (status === 'done') return 'Tamamlandı';
    return 'Yapılacak';
  }

  priorityLabel(priority: string): string {
    return this.priorityLabels[priority] ?? priority;
  }

  priorityStyle(priority: string) {
    return MyWork.PRIORITY_COLORS[priority] ?? MyWork.PRIORITY_COLORS['Low'];
  }

  projectStyle(projectId: number) {
    return MyWork.PROJECT_COLORS[Math.abs(projectId) % MyWork.PROJECT_COLORS.length];
  }
}
