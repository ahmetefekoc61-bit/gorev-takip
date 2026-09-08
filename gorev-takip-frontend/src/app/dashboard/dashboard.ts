import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { TaskService, TaskItem } from '../services/task';
import { ProjectService, Project } from '../services/project';
import { TeamService, Team } from '../services/team';
import { AuthService } from '../services/auth';
import { UserDirectoryService, UserSummary, resolveAvatarUrl } from '../services/user-directory';

interface WorkloadStat {
  userId: number;
  fullName: string;
  avatarUrl: string | null;
  open: number;
  overdue: number;
  done: number;
  pct: number;
}

interface TeamStat {
  teamName: string;
  total: number;
  done: number;
  rate: number;
}

/**
 * Genel bakış sayfası: tüm istatistikler zaten var olan /tasks, /projects, /teams
 * uçlarından (backend ekip izolasyonunu kendisi uyguluyor - Admin hepsini, diğerleri
 * sadece kendi ekibini görüyor) çekilip tarayıcıda toplanıyor. Ayrı bir "dashboard"
 * endpoint'ine gerek kalmadı.
 */
@Component({
  selector: 'app-dashboard',
  imports: [CommonModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css'
})
export class Dashboard implements OnInit {
  loading = true;
  errorMessage = '';

  tasks: TaskItem[] = [];
  projects: Project[] = [];
  teams: Team[] = [];
  members: UserSummary[] = [];

  readonly circumference = 2 * Math.PI * 45;

  constructor(
    private taskService: TaskService,
    private projectService: ProjectService,
    private teamService: TeamService,
    private userDirectory: UserDirectoryService,
    public authService: AuthService,
    private cdr: ChangeDetectorRef
  ) { }

  get totalTasks(): number { return this.tasks.length; }
  get totalProjects(): number { return this.projects.length; }
  get todoCount(): number { return this.tasks.filter(t => t.status === 'todo').length; }
  get inProgressCount(): number { return this.tasks.filter(t => t.status === 'in_progress').length; }
  get doneCount(): number { return this.tasks.filter(t => t.status === 'done').length; }

  get completionRate(): number {
    return this.totalTasks === 0 ? 0 : Math.round((this.doneCount / this.totalTasks) * 100);
  }

  get overdueCount(): number {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    return this.tasks.filter(t => t.dueDate && t.status !== 'done' && new Date(t.dueDate) < today).length;
  }

  get statusSegments() {
    const total = this.totalTasks || 1;
    const todoPct = (this.todoCount / total) * 100;
    const progressPct = (this.inProgressCount / total) * 100;
    const donePct = (this.doneCount / total) * 100;
    return [
      { label: 'Yapılacak', color: '#f97316', pct: todoPct, offset: 0, count: this.todoCount },
      { label: 'Devam Ediyor', color: '#eab308', pct: progressPct, offset: todoPct, count: this.inProgressCount },
      { label: 'Tamamlandı', color: '#22c55e', pct: donePct, offset: todoPct + progressPct, count: this.doneCount }
    ];
  }

  get priorityBars() {
    const order = ['Critical', 'High', 'Medium', 'Low'];
    const labels: Record<string, string> = { Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' };
    const colors: Record<string, string> = { Critical: '#b91c1c', High: '#c2410c', Medium: '#854d0e', Low: '#4338ca' };
    const max = Math.max(1, ...order.map(p => this.tasks.filter(t => t.priority === p).length));

    return order.map(p => {
      const count = this.tasks.filter(t => t.priority === p).length;
      return { label: labels[p], color: colors[p], count, pct: (count / max) * 100 };
    });
  }

  /** Sadece Admin görür - tüm ekiplerin tamamlanma oranını karşılaştırır. */
  get teamStats(): TeamStat[] {
    return this.teams.map(team => {
      const teamProjectIds = this.projects.filter(p => p.teamId === team.id).map(p => p.id);
      const teamTasks = this.tasks.filter(t => teamProjectIds.includes(t.projectId));
      const done = teamTasks.filter(t => t.status === 'done').length;
      const rate = teamTasks.length === 0 ? 0 : Math.round((done / teamTasks.length) * 100);
      return { teamName: team.name, total: teamTasks.length, done, rate };
    });
  }

  /**
   * Kişi bazında iş yükü: "kim kaç açık iş taşıyor" ekip liderinin en sık sorduğu soru
   * ve şu ana kadar cevabı için kullanıcıları tek tek filtrelemek gerekiyordu.
   * Çubuk, en çok yüklü kişiye göre oranlanıyor - mutlak sayı değil dağılım önemli.
   */
  get workloadStats(): WorkloadStat[] {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const stats = this.members.map(m => {
      const mine = this.tasks.filter(t => t.assignedToId === m.id);
      const open = mine.filter(t => t.status !== 'done');
      return {
        userId: m.id,
        fullName: m.fullName,
        avatarUrl: m.avatarUrl,
        open: open.length,
        overdue: open.filter(t => t.dueDate && new Date(t.dueDate) < today).length,
        done: mine.filter(t => t.status === 'done').length,
        pct: 0
      };
    });

    const withWork = stats.filter(s => s.open > 0 || s.done > 0);
    const max = Math.max(1, ...withWork.map(s => s.open));

    return withWork
      .map(s => ({ ...s, pct: (s.open / max) * 100 }))
      .sort((a, b) => b.open - a.open || b.overdue - a.overdue);
  }

  /** Atanmamış açık işler - kimsenin üstünde olmayan iş kolayca gözden kaçıyor. */
  get unassignedOpenCount(): number {
    return this.tasks.filter(t => t.assignedToId == null && t.status !== 'done').length;
  }

  initials(fullName: string): string {
    return fullName.split(' ').filter(Boolean).slice(0, 2).map(p => p[0]?.toUpperCase()).join('');
  }

  avatarUrl(url: string | null): string | null {
    return resolveAvatarUrl(url);
  }

  avatarColor(userId: number): string {
    const colors = ['#6366f1', '#0ea5e9', '#f59e0b', '#10b981', '#ec4899', '#8b5cf6'];
    return colors[Math.abs(userId) % colors.length];
  }

  dashArray(pct: number): string {
    const length = (pct / 100) * this.circumference;
    return `${length} ${this.circumference - length}`;
  }

  dashOffset(offsetPct: number): number {
    return -(offsetPct / 100) * this.circumference;
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    forkJoin({
      tasks: this.taskService.getTasks(),
      projects: this.projectService.getProjects(),
      teams: this.teamService.getTeams(),
      members: this.userDirectory.getUsers()
    }).subscribe({
      next: ({ tasks, projects, teams, members }) => {
        this.tasks = tasks;
        this.projects = projects;
        this.teams = teams;
        this.members = members;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Dashboard verileri yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }
}
