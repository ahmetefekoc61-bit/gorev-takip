import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Sidebar } from '../sidebar/sidebar';
import { Topbar } from '../topbar/topbar';

/**
 * Giriş yapmış kullanıcının gördüğü tüm sayfaların (Pano, Projeler, Ekipler) ortak
 * çerçevesi: sol tarafta sabit sidebar, üstte arama/çıkış çubuğu, ortada router-outlet.
 */
@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, Sidebar, Topbar],
  templateUrl: './layout.html',
  styleUrl: './layout.css'
})
export class Layout { }
