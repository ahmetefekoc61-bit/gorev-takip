import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth';
import { SearchService } from '../../services/search';
import { NotificationService, AppNotification } from '../../services/notification';

@Component({
  selector: 'app-topbar',
  imports: [CommonModule, FormsModule],
  templateUrl: './topbar.html',
  styleUrl: './topbar.css'
})
export class Topbar implements OnInit {
  searchValue = '';

  notifications: AppNotification[] = [];
  panelOpen = false;
  loadFailed = false;

  get unreadCount(): number {
    return this.notifications.filter(n => !n.isRead).length;
  }

  constructor(
    private searchService: SearchService,
    private authService: AuthService,
    private notificationService: NotificationService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    // Rozet sayısının panel hiç açılmasa bile görünmesi için girişte bir kez çekiyoruz.
    this.loadNotifications();
  }

  loadNotifications(): void {
    this.loadFailed = false;
    this.notificationService.getNotifications().subscribe({
      next: (list) => {
        this.notifications = list;
        this.cdr.markForCheck();
      },
      // Hata dalı olmadığında bildirimler yüklenemediğinde panel "Henüz bildirim yok"
      // gösteriyordu; yani hata, boş liste gibi görünüyordu.
      error: () => {
        this.loadFailed = true;
        this.cdr.markForCheck();
      }
    });
  }

  togglePanel(): void {
    this.panelOpen = !this.panelOpen;
    if (this.panelOpen) {
      this.loadNotifications();
    }
  }

  /**
   * Bildirime tıklayınca hem okundu işaretliyor hem de ilgili göreve gidiyor.
   * Önceden yalnızca okundu işaretleniyordu; kullanıcı bildirimin hangi görevle ilgili
   * olduğunu anlasa bile ona ulaşmanın bir yolu yoktu.
   */
  openNotification(n: AppNotification): void {
    if (!n.isRead) {
      this.notificationService.markRead(n.id).subscribe({
        next: () => { n.isRead = true; this.cdr.markForCheck(); },
        error: () => { /* okundu işaretlenemese de yönlendirme yapılmalı */ }
      });
    }

    if (n.taskItemId) {
      this.panelOpen = false;
      this.router.navigate(['/tasks', n.taskItemId]);
    }

    this.cdr.markForCheck();
  }

  markAllRead(): void {
    this.notificationService.markAllRead().subscribe({
      next: () => {
        this.notifications.forEach(n => n.isRead = true);
        this.cdr.markForCheck();
      }
    });
  }

  onSearchInput(): void {
    this.searchService.setTerm(this.searchValue);
  }

  logout(): void {
    this.authService.logout();
    this.searchService.clear();
    this.router.navigate(['/login']);
  }
}
