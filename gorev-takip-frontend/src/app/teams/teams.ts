import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TeamService, Team } from '../services/team';
import { UserDirectoryService, UserRole, UserSummary, resolveAvatarUrl } from '../services/user-directory';
import { AuthService } from '../services/auth';
import { SearchService } from '../services/search';

interface TeamGroup {
  team: Team;
  members: UserSummary[];
}

@Component({
  selector: 'app-teams',
  imports: [CommonModule, FormsModule],
  templateUrl: './teams.html',
  styleUrl: './teams.css'
})
export class Teams implements OnInit {
  loading = true;
  errorMessage = '';
  successMessage = '';

  teamGroups: TeamGroup[] = [];
  unassignedUsers: UserSummary[] = [];

  newTeamName = '';
  creating = false;

  editingTeamId: number | null = null;
  editingTeamName = '';
  savingTeamName = false;

  uploadingUserId: number | null = null;
  searchTerm = '';

  readonly roles: { value: UserRole; label: string }[] = [
    { value: 'Admin', label: 'Yönetici' },
    { value: 'TeamLeader', label: 'Ekip Lideri' },
    { value: 'TeamMember', label: 'Ekip Üyesi' }
  ];

  // Üye listesinde Yönetici ve Ekip Lideri en üstte görünsün diye rol önceliği.
  private readonly rolePriority: Record<UserRole, number> = {
    Admin: 0,
    TeamLeader: 1,
    TeamMember: 2
  };

  constructor(
    private teamService: TeamService,
    private userDirectory: UserDirectoryService,
    public authService: AuthService,
    private searchService: SearchService,
    private cdr: ChangeDetectorRef
  ) {
    // Topbar'daki arama kutusu ile bu sayfa arasındaki bağlantı - pano'daki aynı desen.
    this.searchService.term$.pipe(takeUntilDestroyed()).subscribe(term => {
      this.searchTerm = term;
      this.cdr.markForCheck();
    });
  }

  get isTeamLeader(): boolean {
    return this.authService.getRole() === 'TeamLeader';
  }

  get myTeamId(): number | null {
    return this.authService.getTeamId();
  }

  /**
   * Arama kutusuna yazılan terime göre ekipleri filtreler - ekip adı ya da içindeki
   * herhangi bir üyenin adı eşleşiyorsa o ekip kartı gösterilir (örn. "Bill" yazınca
   * Bill Gates'in olduğu ekip kartı görünür).
   */
  get filteredTeamGroups(): TeamGroup[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) return this.teamGroups;
    return this.teamGroups.filter(g =>
      g.team.name.toLowerCase().includes(term) ||
      g.members.some(m => m.fullName.toLowerCase().includes(term))
    );
  }

  /**
   * Ekip lideri sadece kendi ekibindeki (ya da ekipsiz) kullanıcıları ekleyip
   * çıkarabilir - rol değiştiremez, başka ekiplere dokunamaz. Backend de aynı
   * kuralı zorunlu kılıyor (UsersController.UpdateUserRole), burası sadece
   * arayüzde doğru kontrolleri göstermek için.
   */
  canManageMembership(user: UserSummary): boolean {
    if (this.authService.isAdmin()) return true;
    if (!this.isTeamLeader) return false;
    return user.teamId === this.myTeamId || user.teamId === null;
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    forkJoin({
      teams: this.teamService.getTeams(),
      users: this.userDirectory.getUsers()
    }).subscribe({
      next: ({ teams, users }) => {
        this.teamGroups = teams.map(team => ({
          team,
          members: users
            .filter(u => u.teamId === team.id)
            .sort((a, b) => this.rolePriority[a.role] - this.rolePriority[b.role] || a.fullName.localeCompare(b.fullName, 'tr'))
        }));
        this.unassignedUsers = users.filter(u => u.teamId === null);
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Ekipler yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  createTeam(): void {
    const name = this.newTeamName.trim();
    if (!name) return;

    this.creating = true;
    this.teamService.createTeam(name).subscribe({
      next: () => {
        this.creating = false;
        this.newTeamName = '';
        this.load();
      },
      error: () => {
        this.creating = false;
        this.errorMessage = 'Ekip oluşturulurken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  deleteTeam(team: Team): void {
    if (!confirm(`"${team.name}" ekibini silmek istediğinize emin misiniz?`)) return;

    this.teamService.deleteTeam(team.id).subscribe({
      next: () => this.load(),
      error: () => {
        this.errorMessage = 'Ekip silinirken bir hata oluştu (üyeleri veya projeleri olabilir).';
        this.cdr.markForCheck();
      }
    });
  }

  startEditTeam(team: Team): void {
    this.editingTeamId = team.id;
    this.editingTeamName = team.name;
  }

  cancelEditTeam(): void {
    this.editingTeamId = null;
    this.editingTeamName = '';
  }

  saveTeamName(team: Team): void {
    const name = this.editingTeamName.trim();
    if (!name || name === team.name) {
      this.cancelEditTeam();
      return;
    }

    this.savingTeamName = true;
    this.teamService.updateTeam(team.id, name).subscribe({
      next: () => {
        this.savingTeamName = false;
        this.cancelEditTeam();
        this.load();
      },
      error: () => {
        this.savingTeamName = false;
        this.errorMessage = 'Ekip adı güncellenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  changeRole(user: UserSummary, role: UserRole): void {
    this.userDirectory.updateUserRole(user.id, role, user.teamId).subscribe({
      next: () => {
        this.showSuccess(`${user.fullName} için rol güncellendi.`);
        this.load();
      },
      error: () => {
        this.errorMessage = 'Rol güncellenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * Bir kullanıcıyı başka bir ekibe taşır ya da (teamId null ise) ekipten tamamen çıkarır.
   * Hem ekip içindeki üyeler hem de "Ekibi Olmayanlar" listesindeki kullanıcılar için kullanılıyor.
   */
  changeTeam(user: UserSummary, teamId: number | null): void {
    this.userDirectory.updateUserRole(user.id, user.role, teamId).subscribe({
      next: () => {
        const message = teamId === null
          ? `${user.fullName} ekipten çıkarıldı.`
          : `${user.fullName} için ekip güncellendi.`;
        this.showSuccess(message);
        this.load();
      },
      error: () => {
        this.errorMessage = 'Ekip güncellenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  roleLabel(role: UserRole): string {
    return this.roles.find(r => r.value === role)?.label ?? role;
  }

  avatarUrl(user: UserSummary): string | null {
    return resolveAvatarUrl(user.avatarUrl);
  }

  /**
   * Avatar üzerine tıklanınca açılan gizli <input type="file">'dan seçilen dosyayı
   * kullanıcının profil fotoğrafı olarak yükler. Sadece Admin için görünür bir aksiyon.
   */
  onAvatarSelected(user: UserSummary, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.uploadingUserId = user.id;
    this.userDirectory.uploadAvatar(user.id, file).subscribe({
      next: () => {
        this.uploadingUserId = null;
        this.showSuccess(`${user.fullName} için fotoğraf güncellendi.`);
        this.load();
      },
      error: () => {
        this.uploadingUserId = null;
        this.errorMessage = 'Fotoğraf yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  initials(fullName: string): string {
    return fullName
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase())
      .join('');
  }

  private showSuccess(message: string): void {
    this.successMessage = message;
    this.cdr.markForCheck();
    setTimeout(() => {
      this.successMessage = '';
      this.cdr.markForCheck();
    }, 2500);
  }
}
