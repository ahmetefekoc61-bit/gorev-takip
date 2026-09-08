import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth';

@Component({
  selector: 'app-reset-password',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.css'
})
export class ResetPassword implements OnInit {
  email = '';
  token = '';
  newPassword = '';
  loading = false;
  errorMessage = '';
  infoMessage = '';

  constructor(
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    // /forgot-password sayfasından e-posta query param olarak gelir, kullanıcı tekrar yazmak zorunda kalmaz.
    const emailFromQuery = this.route.snapshot.queryParamMap.get('email');
    if (emailFromQuery) {
      this.email = emailFromQuery;
      this.infoMessage = `${emailFromQuery} adresine gönderdiğimiz kodu girin.`;
    }
  }

  resetPassword(): void {
    this.errorMessage = '';
    this.loading = true;

    this.authService.resetPassword({
      email: this.email,
      token: this.token,
      newPassword: this.newPassword
    }).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigate(['/login']);
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Kod geçersiz veya süresi dolmuş.';
        // Proje zoneless çalışıyor; async callback sonrası render'ı elle tetikliyoruz.
        this.cdr.markForCheck();
      }
    });
  }
}
